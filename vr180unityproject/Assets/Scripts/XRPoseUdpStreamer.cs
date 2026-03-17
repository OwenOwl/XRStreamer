using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.XR;

public class XRPoseUdpStreamer : MonoBehaviour
{
    [Header("UDP")]
    public string targetIp = "255.255.255.255";
    public int targetPort = 5005;
    public bool enableBroadcast = true;

    [Header("Streaming")]
    public int targetHz = 120;
    public bool useUnscaledTime = true;

    [Header("Device Refresh")]
    public float refreshInterval = 1.0f;
    public bool verboseDeviceLogs = true;

    private UdpClient udp;
    private IPEndPoint remoteEndPoint;

    private ulong frameId = 0;
    private float sendInterval;
    private float nextSendTime;
    private float nextRefreshTime;

    private InputDevice hmd;
    private InputDevice leftController;
    private InputDevice rightController;

    void OnEnable()
    {
        InputDevices.deviceConnected += OnDeviceConnected;
        InputDevices.deviceDisconnected += OnDeviceDisconnected;
        InputDevices.deviceConfigChanged += OnDeviceConfigChanged;
    }

    void OnDisable()
    {
        InputDevices.deviceConnected -= OnDeviceConnected;
        InputDevices.deviceDisconnected -= OnDeviceDisconnected;
        InputDevices.deviceConfigChanged -= OnDeviceConfigChanged;

        if (udp != null)
        {
            udp.Close();
            udp = null;
        }
    }

    void OnDestroy()
    {
        if (udp != null)
        {
            udp.Close();
            udp = null;
        }
    }

    void Start()
    {
        sendInterval = 1.0f / Mathf.Max(1, targetHz);
        nextSendTime = GetNow();
        nextRefreshTime = GetNow();

        try
        {
            udp = new UdpClient();
            udp.EnableBroadcast = enableBroadcast;
            remoteEndPoint = new IPEndPoint(IPAddress.Parse(targetIp), targetPort);

            Debug.Log($"[XRPoseUdpStreamer] UDP -> {targetIp}:{targetPort}, broadcast={enableBroadcast}, targetHz={targetHz}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[XRPoseUdpStreamer] UDP init failed: {e}");
            enabled = false;
            return;
        }

        RefreshDevices();
    }

    void Update()
    {
        float now = GetNow();

        if (now >= nextRefreshTime)
        {
            nextRefreshTime = now + Mathf.Max(0.1f, refreshInterval);
            RefreshDevices();
        }

        if (now < nextSendTime)
            return;

        nextSendTime += sendInterval;
        if (now > nextSendTime + 0.5f)
            nextSendTime = now + sendInterval;

        if (!IsDeviceUsable(hmd) || !IsDeviceUsable(leftController) || !IsDeviceUsable(rightController))
        {
            RefreshDevices();
        }

        frameId++;

        bool okHmd = TryGetPose(hmd, out var hmdPos, out var hmdQuat);
        bool okLeft = TryGetPose(leftController, out var leftPos, out var leftQuat);
        bool okRight = TryGetPose(rightController, out var rightPos, out var rightQuat);

        Vector2 leftStick = TryGetAxis2D(leftController, CommonUsages.primary2DAxis);
        Vector2 rightStick = TryGetAxis2D(rightController, CommonUsages.primary2DAxis);

        float leftTrigger = TryGetAxis1D(leftController, CommonUsages.trigger);
        float rightTrigger = TryGetAxis1D(rightController, CommonUsages.trigger);

        float leftGrip = TryGetAxis1D(leftController, CommonUsages.grip);
        float rightGrip = TryGetAxis1D(rightController, CommonUsages.grip);

        int leftPrimaryButton = TryGetBool01(leftController, CommonUsages.primaryButton);
        int leftSecondaryButton = TryGetBool01(leftController, CommonUsages.secondaryButton);
        int leftStickClick = TryGetBool01(leftController, CommonUsages.primary2DAxisClick);

        int rightPrimaryButton = TryGetBool01(rightController, CommonUsages.primaryButton);
        int rightSecondaryButton = TryGetBool01(rightController, CommonUsages.secondaryButton);
        int rightStickClick = TryGetBool01(rightController, CommonUsages.primary2DAxisClick);

        if (!okHmd)   { hmdPos = Vector3.zero;  hmdQuat = Quaternion.identity; }
        if (!okLeft)  { leftPos = Vector3.zero; leftQuat = Quaternion.identity; }
        if (!okRight) { rightPos = Vector3.zero; rightQuat = Quaternion.identity; }

        string msg = string.Format(
            CultureInfo.InvariantCulture,
            "FRAME,{0}," +
            "HMD,{1},{2},{3},{4},{5},{6},{7}," +
            "LEFTHAND,{8},{9},{10},{11},{12},{13},{14}," +
            "LEFTSTICK,{15},{16}," +
            "LEFTTRIGGER,{17}," +
            "LEFTGRIP,{18}," +
            "LEFTKEYS,{19},{20},{21}," +
            "RIGHTHAND,{22},{23},{24},{25},{26},{27},{28}," +
            "RIGHTSTICK,{29},{30}," +
            "RIGHTTRIGGER,{31}," +
            "RIGHTGRIP,{32}," +
            "RIGHTKEYS,{33},{34},{35}",
            frameId,

            hmdPos.x, hmdPos.y, hmdPos.z, hmdQuat.x, hmdQuat.y, hmdQuat.z, hmdQuat.w,

            leftPos.x, leftPos.y, leftPos.z, leftQuat.x, leftQuat.y, leftQuat.z, leftQuat.w,
            leftStick.x, leftStick.y,
            leftTrigger,
            leftGrip,
            leftPrimaryButton, leftSecondaryButton, leftStickClick,

            rightPos.x, rightPos.y, rightPos.z, rightQuat.x, rightQuat.y, rightQuat.z, rightQuat.w,
            rightStick.x, rightStick.y,
            rightTrigger,
            rightGrip,
            rightPrimaryButton, rightSecondaryButton, rightStickClick
        );

        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(msg);
            udp.Send(bytes, bytes.Length, remoteEndPoint);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[XRPoseUdpStreamer] UDP send failed: {e.Message}");
        }
    }

    private void OnDeviceConnected(InputDevice device)
    {
        if (verboseDeviceLogs)
            Debug.Log($"[XRPoseUdpStreamer] XR device connected: name={device.name}, role={device.role}, chars={device.characteristics}");

        RefreshDevices();
    }

    private void OnDeviceDisconnected(InputDevice device)
    {
        if (verboseDeviceLogs)
            Debug.Log($"[XRPoseUdpStreamer] XR device disconnected: name={device.name}, role={device.role}, chars={device.characteristics}");

        RefreshDevices();
    }

    private void OnDeviceConfigChanged(InputDevice device)
    {
        if (verboseDeviceLogs)
            Debug.Log($"[XRPoseUdpStreamer] XR device config changed: name={device.name}, role={device.role}, chars={device.characteristics}");

        RefreshDevices();
    }

    private float GetNow()
    {
        return useUnscaledTime ? Time.unscaledTime : Time.time;
    }

    private void RefreshDevices()
    {
        hmd = FindHeadDevice();
        leftController = FindLeftController();
        rightController = FindRightController();
    }

    private bool IsDeviceUsable(InputDevice device)
    {
        return device.isValid;
    }

    private InputDevice FindHeadDevice()
    {
        var devices = new List<InputDevice>();

        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.HeadMounted | InputDeviceCharacteristics.TrackedDevice,
            devices
        );

        if (devices.Count > 0)
            return devices[0];

        return InputDevices.GetDeviceAtXRNode(XRNode.Head);
    }

    private InputDevice FindLeftController()
    {
        var devices = new List<InputDevice>();

        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Left |
            InputDeviceCharacteristics.Controller |
            InputDeviceCharacteristics.HeldInHand |
            InputDeviceCharacteristics.TrackedDevice,
            devices
        );

        if (devices.Count > 0)
            return devices[0];

        return InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
    }

    private InputDevice FindRightController()
    {
        var devices = new List<InputDevice>();

        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right |
            InputDeviceCharacteristics.Controller |
            InputDeviceCharacteristics.HeldInHand |
            InputDeviceCharacteristics.TrackedDevice,
            devices
        );

        if (devices.Count > 0)
            return devices[0];

        return InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    private bool TryGetPose(InputDevice device, out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;

        if (!device.isValid)
            return false;

        bool trackedOk = true;
        if (device.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked))
            trackedOk = isTracked;

        bool posOk = device.TryGetFeatureValue(CommonUsages.devicePosition, out pos);
        bool rotOk = device.TryGetFeatureValue(CommonUsages.deviceRotation, out rot);

        return trackedOk && posOk && rotOk;
    }

    private Vector2 TryGetAxis2D(InputDevice device, InputFeatureUsage<Vector2> usage)
    {
        if (!device.isValid)
            return Vector2.zero;

        if (device.TryGetFeatureValue(usage, out Vector2 value))
            return value;

        return Vector2.zero;
    }

    private float TryGetAxis1D(InputDevice device, InputFeatureUsage<float> usage)
    {
        if (!device.isValid)
            return 0f;

        if (device.TryGetFeatureValue(usage, out float value))
            return Mathf.Clamp01(value);

        return 0f;
    }

    private int TryGetBool01(InputDevice device, InputFeatureUsage<bool> usage)
    {
        if (!device.isValid)
            return 0;

        return device.TryGetFeatureValue(usage, out bool value) && value ? 1 : 0;
    }
}