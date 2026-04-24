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

    [Header("IMU")]
    public IMUSource imuSource;

    [Header("Debug UI")]
    public TextMesh[] debugTextMeshes;
    public Color idleColor = Color.white;
    public Color warningColor = Color.yellow;
    public Color recordingColor = Color.red;
    public float popupDuration = 1.5f;

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

    void OnEnable()
    {
        
    }

    void OnDisable()
    {
        
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

        Quaternion imuQ = imuSource.GetImuRotation();
        
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

        imuEuler = imuSource.GetEulerDeg();

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