using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

public class IMUSource : MonoBehaviour
{
    [Header("Serial")]
    public string portName = "COM3";
    public int baudRate = 115200;

    [Header("Parsed WT61C Data (debug)")]
    public Vector3 accel_g;              // from 0x51, unit: g
    public Vector3 angleVel_deg_s;       // from 0x52, unit: deg/s
    public Vector3 eulerDeg;             // from 0x53, x=roll y=pitch z=yaw
    public float temperatureC;

    private SerialPort serialPort;
    private Thread readThread;
    private volatile bool running = false;

    private readonly object dataLock = new object();

    public readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();

    private struct TimestampedRotation
    {
        public double time;
        public Quaternion rotation;
    }
    private readonly Queue<TimestampedRotation> rotationBuffer = new Queue<TimestampedRotation>();

    void OnEnable()
    {
        
    }

    void OnDisable()
    {
        ClosePort();
    }

    void Start()
    {
        
    }

    void Update()
    {

    }

    public void OpenPort()
    {
        if (running) return;

        try
        {
            serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
            serialPort.ReadTimeout = 100;
            serialPort.Open();

            clock.Start();
            running = true;
            readThread = new Thread(ReadLoop);
            readThread.IsBackground = true;
            readThread.Start();

            Debug.Log($"[IMUSource] Opened {portName} @ {baudRate}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[IMUSource] Failed to open serial port {portName}: {e}");
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
            Debug.LogWarning($"[IMUSource] Thread join warning: {e}");
        }

        try
        {
            if (serialPort != null && serialPort.IsOpen)
                serialPort.Close();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[IMUSource] Port close warning: {e}");
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
                Debug.LogWarning($"[IMUSource] Serial read warning: {e}");
                Thread.Sleep(10);
            }
        }
    }

    public bool HasData()
    {
        lock (dataLock)
        {
            return rotationBuffer.Count > 0;
        }
    }

    public Vector3 GetEulerDeg()
    {
        lock (dataLock)
        {
            return eulerDeg;
        }
    }

    public Quaternion GetImuRotation(double? targetTime = null)
    {
        lock (dataLock)
        {
            if (targetTime == null)
                targetTime = clock.Elapsed.TotalSeconds;

            Quaternion imuRotation = Quaternion.identity;
            
            while (rotationBuffer.Count > 1 && rotationBuffer.Peek().time <= targetTime)
                rotationBuffer.Dequeue();

            if (rotationBuffer.Count > 0)
                imuRotation = rotationBuffer.Peek().rotation;

            return imuRotation;
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
                        Quaternion.AngleAxis(yaw,   -Vector3.up) *
                        Quaternion.AngleAxis(pitch, Vector3.right) *
                        Quaternion.AngleAxis(roll,  -Vector3.forward);

                    rotationBuffer.Enqueue(new TimestampedRotation
                    {
                        time = clock.Elapsed.TotalSeconds,
                        rotation = imuRotation
                    });

                    break;

                default:
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