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

        // Initial try, but do NOT assume devices are ready now.
        RefreshDevices();
    }

    void Update()
    {
        float now = GetNow();

        // Periodically refresh devices because XR devices may appear late.
        if (now >= nextRefreshTime)
        {
            nextRefreshTime = now + Mathf.Max(0.1f, refreshInterval);
            RefreshDevices();
        }

        // streaming clock
        if (now < nextSendTime)
            return;

        nextSendTime += sendInterval;
        if (now > nextSendTime + 0.5f)
            nextSendTime = now + sendInterval;

        // If any device became invalid, try refreshing immediately.
        if (!IsDeviceUsable(hmd) || !IsDeviceUsable(leftController) || !IsDeviceUsable(rightController))
        {
            RefreshDevices();
        }

        frameId++;

        bool okHmd = TryGetPose(hmd, out var hmdPos, out var hmdQuat);
        bool okLeft = TryGetPose(leftController, out var leftPos, out var leftQuat);
        bool okRight = TryGetPose(rightController, out var rightPos, out var rightQuat);

        int leftButton = TryGetButtonMask(leftController);
        int rightButton = TryGetButtonMask(rightController);

        if (!okHmd)   { hmdPos = Vector3.zero;  hmdQuat = Quaternion.identity; }
        if (!okLeft)  { leftPos = Vector3.zero; leftQuat = Quaternion.identity; }
        if (!okRight) { rightPos = Vector3.zero; rightQuat = Quaternion.identity; }

        string msg = string.Format(
            CultureInfo.InvariantCulture,
            "FRAME,{0},HMD,{1},{2},{3},{4},{5},{6},{7},LEFTHAND,{8},{9},{10},{11},{12},{13},{14},LEFTBUTTON,{15},RIGHTHAND,{16},{17},{18},{19},{20},{21},{22},RIGHTBUTTON,{23}",
            frameId,
            hmdPos.x, hmdPos.y, hmdPos.z, hmdQuat.x, hmdQuat.y, hmdQuat.z, hmdQuat.w,
            leftPos.x, leftPos.y, leftPos.z, leftQuat.x, leftQuat.y, leftQuat.z, leftQuat.w,
            leftButton,
            rightPos.x, rightPos.y, rightPos.z, rightQuat.x, rightQuat.y, rightQuat.z, rightQuat.w,
            rightButton
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

    private int TryGetButtonMask(InputDevice device)
    {
        if (!device.isValid)
            return 0;

        int mask = 0;

        // bit0 primaryButton
        // bit1 secondaryButton
        // bit2 triggerButton
        // bit3 gripButton
        // bit4 primary2DAxisClick

        if (TryGetBool(device, CommonUsages.primaryButton))      mask |= (1 << 0);
        if (TryGetBool(device, CommonUsages.secondaryButton))    mask |= (1 << 1);
        if (TryGetBool(device, CommonUsages.triggerButton))      mask |= (1 << 2);
        if (TryGetBool(device, CommonUsages.gripButton))         mask |= (1 << 3);
        if (TryGetBool(device, CommonUsages.primary2DAxisClick)) mask |= (1 << 4);

        return mask;
    }

    private bool TryGetBool(InputDevice device, InputFeatureUsage<bool> usage)
    {
        return device.TryGetFeatureValue(usage, out bool value) && value;
    }
}