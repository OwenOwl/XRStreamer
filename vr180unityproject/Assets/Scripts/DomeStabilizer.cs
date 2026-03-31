using System;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

public class DomeStabilizer : MonoBehaviour
{
    [Header("Serial")]
    public string portName = "COM3";
    public int baudRate = 115200;
    public bool autoOpenOnStart = true;

    [Header("Debug / Inspector")]
    public Quaternion robotRotation = Quaternion.identity;

    [Header("Parsed WT61C Data (debug)")]
    public Vector3 accel_g;              // from 0x51, unit: g
    public Vector3 angleVel_deg_s;       // from 0x52, unit: deg/s
    public Vector3 eulerDeg;             // from 0x53, x=roll y=pitch z=yaw
    public float temperatureC;

    private SerialPort serialPort;
    private Thread readThread;
    private volatile bool running = false;

    private readonly object dataLock = new object();

    private bool hasReferenceRotation = false;
    private Quaternion referenceRotation = Quaternion.identity;

    void Start()
    {
        if (autoOpenOnStart)
            OpenPort();
    }

    void OnDestroy()
    {
        ClosePort();
    }

    void OnApplicationQuit()
    {
        ClosePort();
    }

    void Update()
    {
        Quaternion q;
        lock (dataLock)
        {
            q = robotRotation;
        }

        transform.localRotation = q;
    }

    public void SetRobotRotation(Quaternion q)
    {
        lock (dataLock)
        {
            robotRotation = q;
        }
    }

    public void OpenPort()
    {
        if (running) return;

        try
        {
            serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
            serialPort.ReadTimeout = 100;
            serialPort.Open();

            running = true;
            readThread = new Thread(ReadLoop);
            readThread.IsBackground = true;
            readThread.Start();

            Debug.Log($"[DomeStabilizer] Opened {portName} @ {baudRate}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[DomeStabilizer] Failed to open serial port {portName}: {e}");
        }
    }

    public void ClosePort()
    {
        running = false;

        try
        {
            if (readThread != null && readThread.IsAlive)
                readThread.Join(500);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DomeStabilizer] Thread join warning: {e}");
        }

        try
        {
            if (serialPort != null && serialPort.IsOpen)
                serialPort.Close();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DomeStabilizer] Port close warning: {e}");
        }

        readThread = null;
        serialPort = null;
    }

    private void ReadLoop()
    {
        byte[] frame = new byte[11];

        while (running && serialPort != null && serialPort.IsOpen)
        {
            try
            {
                int b = serialPort.ReadByte();
                if (b < 0) continue;

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

                ParseFrame(frame);
            }
            catch (TimeoutException)
            {
                // fine, just continue
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DomeStabilizer] Serial read warning: {e}");
                Thread.Sleep(10);
            }
        }
    }

    private void ParseFrame(byte[] f)
    {
        byte type = f[1];

        short v1 = ToInt16LE(f[2], f[3]);
        short v2 = ToInt16LE(f[4], f[5]);
        short v3 = ToInt16LE(f[6], f[7]);
        short vt = ToInt16LE(f[8], f[9]);

        lock (dataLock)
        {
            switch (type)
            {
                case 0x51:
                    // Acceleration: ±16g
                    // accel_g = new Vector3(
                    //     v1 / 32768f * 16f,
                    //     v2 / 32768f * 16f,
                    //     v3 / 32768f * 16f
                    // );
                    // temperatureC = vt / 340f + 36.25f;
                    break;

                case 0x52:
                    // Angular velocity: ±2000 deg/s
                    // angleVel_deg_s = new Vector3(
                    //     v1 / 32768f * 2000f,
                    //     v2 / 32768f * 2000f,
                    //     v3 / 32768f * 2000f
                    // );
                    // temperatureC = vt / 340f + 36.25f;
                    break;

                case 0x53:
                    // Euler angles: roll, pitch, yaw in degrees
                    float roll = v1 / 32768f * 180f;
                    float pitch = v2 / 32768f * 180f;
                    float yaw = v3 / 32768f * 180f;

                    eulerDeg = new Vector3(roll, pitch, yaw);
                    temperatureC = vt / 340f + 36.25f;

                    Quaternion imuRotation =
                        Quaternion.AngleAxis(roll,  -Vector3.forward) *
                        Quaternion.AngleAxis(pitch, Vector3.right) *
                        Quaternion.AngleAxis(yaw,   -Vector3.up);

                    if (!hasReferenceRotation)
                    {
                        referenceRotation = imuRotation;
                        hasReferenceRotation = true;
                    }

                    imuRotation = imuRotation * Quaternion.Inverse(referenceRotation);
                    robotRotation = imuRotation;
                    break;

                default:
                    // ignore other packet types for now
                    break;
            }
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

    public static string[] GetAvailablePorts()
    {
        return SerialPort.GetPortNames();
    }
}