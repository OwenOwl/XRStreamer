using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using UnityEngine;

public class PoseSequenceRecorder : MonoBehaviour
{
    [Header("Pose Source")]
    public PoseSource poseSource;

    [Header("Sampling")]
    [Tooltip("Fixed recording rate.")]
    public int targetHz = 120;

    [Tooltip("Use unscaled time so capture is independent of Time.timeScale.")]
    public bool useUnscaledTime = true;

    [Header("Record Trigger")]
    [Tooltip("Press right trigger past this threshold to toggle start/stop.")]
    [Range(0f, 1f)]
    public float triggerThreshold = 0.8f;

    [Header("Save")]
    [Tooltip("Subfolder under Application.persistentDataPath.")]
    public string saveSubfolder = "PoseRecordings";

    public string filePrefix = "sequence";

    [Header("IMU Serial")]
    public string portName = "COM3";
    public int baudRate = 115200;

    [Header("Debug UI")]
    public TextMesh[] debugTextMeshes;
    public Color idleColor = Color.white;
    public Color warningColor = Color.yellow;
    public Color recordingColor = Color.red;
    public float popupDuration = 1.5f;

    [Header("IMU Debug")]
    public Vector3 imuEulerDeg;
    public Quaternion imuUnityRotation = Quaternion.identity;
    public float imuTemperatureC;

    [Header("Status Debug")]
    public bool isRecording = false;
    public int recordedFrames = 0;
    public string lastSavedPath = "";

    private bool recorderInitialized = false;

    // Fixed-rate timing
    private float sampleInterval;
    private float nextSampleTime;

    // Trigger edge detect
    private bool prevRightTriggerPressed = false;

    // Debug UI
    private string popupMessage = "";
    private float popupUntilTime = -1f;

    // Recording buffer
    // Each row:
    // [time,
    //  hmd_px,hmd_py,hmd_pz,hmd_qx,hmd_qy,hmd_qz,hmd_qw,
    //  left_px,left_py,left_pz,left_qx,left_qy,left_qz,left_qw,
    //  right_px,right_py,right_pz,right_qx,right_qy,right_qz,right_qw,
    //  imu_qx,imu_qy,imu_qz,imu_qw,
    //  left_trigger]
    private readonly List<float[]> recordedRows = new List<float[]>(8192);

    // IMU serial thread
    private SerialPort serialPort;
    private Thread readThread;
    private volatile bool serialRunning = false;
    private readonly object imuLock = new object();
    void OnEnable()
    {
        
    }

    void OnDisable()
    {
        CloseImuPort();
    }

    void Start()
    {
        
    }

    public void InitializeRecorder()
    {
        if (recorderInitialized)
            return;

        if (poseSource == null)
        {
            Debug.LogError("[PoseSequenceRecorder] poseSource is not assigned.");
            return;
        }

        sampleInterval = 1.0f / Mathf.Max(1, targetHz);
        nextSampleTime = GetNow();

        OpenImuPort();

        Debug.Log($"[PoseSequenceRecorder] Save folder: {GetSaveDirectory()}");

        recorderInitialized = true;
    }

    void Update()
    {
        if (!recorderInitialized || poseSource == null)
            return;
        
        float now = GetNow();

        while (now >= nextSampleTime)
        {
            SampleOnce(nextSampleTime);
            nextSampleTime += sampleInterval;
        }

        // Recover if game stalls for a while
        if (now > nextSampleTime + 0.5f)
            nextSampleTime = now + sampleInterval;
        
        UpdateDebugText();
    }

    private void SampleOnce(float sampleTime)
    {
        bool okHmd   = poseSource.okHmd;
        bool okLeft  = poseSource.okLeft;
        bool okRight = poseSource.okRight;

        Vector3    hmdPos   = poseSource.hmdPos;
        Quaternion hmdRot   = poseSource.hmdQuat;
        Vector3    leftPos  = poseSource.leftPos;
        Quaternion leftRot  = poseSource.leftQuat;
        Vector3    rightPos = poseSource.rightPos;
        Quaternion rightRot = poseSource.rightQuat;

        float rightTrigger = poseSource.rightTrigger;
        float leftTrigger  = poseSource.leftTrigger;
        bool rightTriggerPressed = rightTrigger >= triggerThreshold;

        // Rising edge toggles recording
        if (rightTriggerPressed && !prevRightTriggerPressed)
        {
            if (!isRecording)
                StartRecording();
            else
                StopRecordingAndSave();
        }
        prevRightTriggerPressed = rightTriggerPressed;

        if (!isRecording)
            return;

        Quaternion imuQ;
        lock (imuLock)
        {
            imuQ = imuUnityRotation;
        }

        float[] row = new float[27];
        int k = 0;

        row[k++] = sampleTime;

        // HMD
        row[k++] = hmdPos.x;
        row[k++] = hmdPos.y;
        row[k++] = hmdPos.z;
        row[k++] = hmdRot.x;
        row[k++] = hmdRot.y;
        row[k++] = hmdRot.z;
        row[k++] = hmdRot.w;

        // Left controller
        row[k++] = leftPos.x;
        row[k++] = leftPos.y;
        row[k++] = leftPos.z;
        row[k++] = leftRot.x;
        row[k++] = leftRot.y;
        row[k++] = leftRot.z;
        row[k++] = leftRot.w;

        // Right controller
        row[k++] = rightPos.x;
        row[k++] = rightPos.y;
        row[k++] = rightPos.z;
        row[k++] = rightRot.x;
        row[k++] = rightRot.y;
        row[k++] = rightRot.z;
        row[k++] = rightRot.w;

        // IMU quaternion only
        row[k++] = imuQ.x;
        row[k++] = imuQ.y;
        row[k++] = imuQ.z;
        row[k++] = imuQ.w;

        // Left trigger
        row[k++] = leftTrigger;

        recordedRows.Add(row);
        recordedFrames = recordedRows.Count;
    }

    private void StartRecording()
    {
        recordedRows.Clear();
        recordedFrames = 0;
        isRecording = true;
        ShowPopup("Recording started");
        Debug.Log("[PoseSequenceRecorder] Recording started.");
    }

    private void StopRecordingAndSave()
    {
        isRecording = false;

        if (recordedRows.Count == 0)
        {
            ShowPopup("Stopped, no frames");
            Debug.LogWarning("[PoseSequenceRecorder] Recording stopped, but no frames were captured.");
            return;
        }

        string dir = GetSaveDirectory();
        Directory.CreateDirectory(dir);

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string npyPath = Path.Combine(dir, $"{filePrefix}_{stamp}.npy");
        // string metaPath = Path.Combine(dir, $"{filePrefix}_{stamp}_meta.json");

        SaveNpy(npyPath, recordedRows);
        // SaveMeta(metaPath, npyPath);

        lastSavedPath = npyPath;
        ShowPopup($"Saved {recordedRows.Count} frames");
        Debug.Log($"[PoseSequenceRecorder] Saved {recordedRows.Count} frames to:\n{npyPath}");
    }

    private string GetSaveDirectory()
    {
        return Path.Combine(Application.persistentDataPath, saveSubfolder);
    }

    private void SaveCsv(string path, List<float[]> rows)
    {
        var sb = new StringBuilder(1024 * 1024);

        sb.AppendLine(
            "time," +
            "hmd_px,hmd_py,hmd_pz,hmd_qx,hmd_qy,hmd_qz,hmd_qw," +
            "left_px,left_py,left_pz,left_qx,left_qy,left_qz,left_qw," +
            "right_px,right_py,right_pz,right_qx,right_qy,right_qz,right_qw," +
            "imu_qx,imu_qy,imu_qz,imu_qw," +
            "left_trigger"
        );

        foreach (float[] row in rows)
        {
            for (int i = 0; i < row.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(row[i].ToString("R", CultureInfo.InvariantCulture));
            }
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }

    private void SaveNpy(string path, List<float[]> rows)
    {
        int T = rows.Count;
        if (T == 0) return;

        int D = rows[0].Length;

        using (var fs = new FileStream(path, FileMode.Create))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((byte)0x93);
            bw.Write(Encoding.ASCII.GetBytes("NUMPY"));
            bw.Write((byte)1); // major
            bw.Write((byte)0); // minor

            string header = $"{{'descr': '<f4', 'fortran_order': False, 'shape': ({T}, {D}), }}";

            // pad to 16-byte alignment
            int headerLen = header.Length + 1; // + newline
            int padLen = 16 - ((10 + headerLen) % 16);
            if (padLen == 16) padLen = 0;

            string fullHeader = header + new string(' ', padLen) + "\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(fullHeader);

            bw.Write((ushort)headerBytes.Length);
            bw.Write(headerBytes);

            for (int i = 0; i < T; i++)
            {
                float[] row = rows[i];
                for (int j = 0; j < D; j++)
                {
                    bw.Write(row[j]); // little-endian float32
                }
            }
        }
    }

    private void SaveMeta(string metaPath, string csvPath)
    {
        string json =
            "{\n" +
            $"  \"csv_path\": \"{EscapeJson(csvPath)}\",\n" +
            $"  \"num_frames\": {recordedRows.Count},\n" +
            $"  \"target_hz\": {targetHz},\n" +
            "  \"row_layout\": [\n" +
            "    \"time\",\n" +
            "    \"hmd_pos_xyz\",\n" +
            "    \"hmd_quat_xyzw\",\n" +
            "    \"left_pos_xyz\",\n" +
            "    \"left_quat_xyzw\",\n" +
            "    \"right_pos_xyz\",\n" +
            "    \"right_quat_xyzw\",\n" +
            "    \"imu_quat_xyzw\"\n" +
            "  ],\n" +
            "}\n";

        File.WriteAllText(metaPath, json);
    }

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private float GetNow()
    {
        return useUnscaledTime ? Time.unscaledTime : Time.time;
    }

    // ---------------- IMU ----------------

    public void OpenImuPort()
    {
        if (serialRunning)
            return;

        try
        {
            serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
            serialPort.ReadTimeout = 100;
            serialPort.Open();

            serialRunning = true;
            readThread = new Thread(ReadImuLoop);
            readThread.IsBackground = true;
            readThread.Start();

            Debug.Log($"[PoseSequenceRecorder] IMU serial opened: {portName} @ {baudRate}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[PoseSequenceRecorder] Failed to open IMU serial port {portName}: {e}");
        }
    }

    public void CloseImuPort()
    {
        serialRunning = false;

        try
        {
            if (readThread != null && readThread.IsAlive)
                readThread.Join(500);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PoseSequenceRecorder] IMU thread join warning: {e}");
        }

        try
        {
            if (serialPort != null && serialPort.IsOpen)
                serialPort.Close();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PoseSequenceRecorder] IMU port close warning: {e}");
        }

        readThread = null;
        serialPort = null;
    }

    private void ReadImuLoop()
    {
        byte[] frame = new byte[11];

        while (serialRunning && serialPort != null && serialPort.IsOpen)
        {
            try
            {
                int b = serialPort.ReadByte();
                if (b < 0)
                    continue;

                if ((byte)b != 0x55)
                    continue;

                frame[0] = 0x55;

                int got = 1;
                while (got < 11)
                {
                    int n = serialPort.Read(frame, got, 11 - got);
                    if (n > 0) got += n;
                }

                if (!CheckSum(frame))
                    continue;

                ParseImuFrame(frame);
            }
            catch (TimeoutException)
            {
                // normal
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PoseSequenceRecorder] IMU serial warning: {e.Message}");
                Thread.Sleep(10);
            }
        }
    }

    private void ParseImuFrame(byte[] f)
    {
        byte type = f[1];

        short v1 = ToInt16LE(f[2], f[3]);
        short v2 = ToInt16LE(f[4], f[5]);
        short v3 = ToInt16LE(f[6], f[7]);
        short vt = ToInt16LE(f[8], f[9]);

        if (type != 0x53)
            return;

        float roll = v1 / 32768f * 180f;
        float pitch = v2 / 32768f * 180f;
        float yaw = v3 / 32768f * 180f;
        float temp = vt / 340f + 36.25f;

        // Same Unity-axis conversion style as your DomeStabilizer
        Quaternion qUnity =
            Quaternion.AngleAxis(roll, -Vector3.forward) *
            Quaternion.AngleAxis(pitch, Vector3.right) *
            Quaternion.AngleAxis(yaw, -Vector3.up);

        lock (imuLock)
        {
            imuEulerDeg = new Vector3(roll, pitch, yaw);
            imuTemperatureC = temp;
            imuUnityRotation = NormalizeQuaternion(qUnity);
        }
    }

    private static short ToInt16LE(byte lo, byte hi)
    {
        return (short)((hi << 8) | lo);
    }

    private static bool CheckSum(byte[] f)
    {
        int sum = 0;
        for (int i = 0; i < 10; i++)
            sum += f[i];
        return (byte)(sum & 0xFF) == f[10];
    }

    private static Quaternion NormalizeQuaternion(Quaternion q)
    {
        float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        if (n < 1e-12f)
            return Quaternion.identity;

        float inv = 1f / n;
        return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
    }

    // ---------------- Debug UI ----------------

    private void ShowPopup(string msg)
    {
        popupMessage = msg;
        popupUntilTime = GetNow() + popupDuration;
    }

    private string OkMark(bool ok)
    {
        return ok ? "[OK]" : "[--]";
    }

    private void UpdateDebugText()
    {
        if (debugTextMeshes == null || debugTextMeshes.Length == 0)
            return;
        
        Vector3 imuEuler;

        lock (imuLock)
        {
            imuEuler = imuEulerDeg;
        }

        bool okHmd   = poseSource != null && poseSource.okHmd;
        bool okLeft  = poseSource != null && poseSource.okLeft;
        bool okRight = poseSource != null && poseSource.okRight;
        float leftTrigger = poseSource != null ? poseSource.leftTrigger : 0f;

        string rec = isRecording ? "REC" : "IDLE";
        string line2 = $"HMD {OkMark(okHmd)}   L {OkMark(okLeft)}   R {OkMark(okRight)}";
        string line3 = $"IMU {imuEuler.x:F2}, {imuEuler.y:F2}, {imuEuler.z:F2}    LT {leftTrigger:F2}";

        bool popupActive = GetNow() <= popupUntilTime;
        string text = popupActive
            ? $"{rec}\n{line2}\n{line3}\n{popupMessage}"
            : $"{rec}\n{line2}\n{line3}";

        Color c;
        if (!okHmd || !okLeft || !okRight)
            c = warningColor;
        else if (isRecording)
            c = Color.red;
        else
            c = idleColor;

        foreach (var tm in debugTextMeshes)
        {
            if (tm == null) continue;

            tm.text = text;
            tm.color = c;
        }
    }
}