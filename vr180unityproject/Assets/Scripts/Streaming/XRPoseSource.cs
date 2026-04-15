using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class XRPoseSource : PoseSource
{
    private float nextRefreshTime;

    private InputDevice hmd;
    private InputDevice leftController;
    private InputDevice rightController;

    void OnEnable()
    {
        InputDevices.deviceConnected += OnDeviceConnected;
        InputDevices.deviceDisconnected += OnDeviceDisconnected;
        InputDevices.deviceConfigChanged += OnDeviceConfigChanged;

        nextRefreshTime = GetNow();
        RefreshDevices();
    }

    void OnDisable()
    {
        InputDevices.deviceConnected -= OnDeviceConnected;
        InputDevices.deviceDisconnected -= OnDeviceDisconnected;
        InputDevices.deviceConfigChanged -= OnDeviceConfigChanged;
    }

    void Update()
    {
        float now = GetNow();

        if (now >= nextRefreshTime)
        {
            nextRefreshTime = now + Mathf.Max(0.1f, refreshInterval);
            RefreshDevices();
        }

        if (!IsDeviceUsable(hmd) || !IsDeviceUsable(leftController) || !IsDeviceUsable(rightController))
            RefreshDevices();

        okHmd = TryGetPose(hmd, out hmdPos, out hmdQuat);
        okLeft = TryGetPose(leftController, out leftPos, out leftQuat);
        okRight = TryGetPose(rightController, out rightPos, out rightQuat);
        ok = okHmd || okLeft || okRight;

        leftStick = TryGetAxis2D(leftController, CommonUsages.primary2DAxis);
        rightStick = TryGetAxis2D(rightController, CommonUsages.primary2DAxis);

        leftTrigger = TryGetAxis1D(leftController, CommonUsages.trigger);
        rightTrigger = TryGetAxis1D(rightController, CommonUsages.trigger);

        leftGrip = TryGetAxis1D(leftController, CommonUsages.grip);
        rightGrip = TryGetAxis1D(rightController, CommonUsages.grip);

        leftPrimaryButton = TryGetBool01(leftController, CommonUsages.primaryButton);
        leftSecondaryButton = TryGetBool01(leftController, CommonUsages.secondaryButton);
        leftStickClick = TryGetBool01(leftController, CommonUsages.primary2DAxisClick);

        rightPrimaryButton = TryGetBool01(rightController, CommonUsages.primaryButton);
        rightSecondaryButton = TryGetBool01(rightController, CommonUsages.secondaryButton);
        rightStickClick = TryGetBool01(rightController, CommonUsages.primary2DAxisClick);

        if (!okHmd)
        {
            hmdPos = Vector3.zero;
            hmdQuat = Quaternion.identity;
        }

        if (!okLeft)
        {
            leftPos = Vector3.zero;
            leftQuat = Quaternion.identity;
        }

        if (!okRight)
        {
            rightPos = Vector3.zero;
            rightQuat = Quaternion.identity;
        }
    }

    private void OnDeviceConnected(InputDevice device)
    {
        if (verboseDeviceLogs)
            Debug.Log($"[PoseSource] XR device connected: name={device.name}, role={device.role}, chars={device.characteristics}");

        RefreshDevices();
    }

    private void OnDeviceDisconnected(InputDevice device)
    {
        if (verboseDeviceLogs)
            Debug.Log($"[PoseSource] XR device disconnected: name={device.name}, role={device.role}, chars={device.characteristics}");

        RefreshDevices();
    }

    private void OnDeviceConfigChanged(InputDevice device)
    {
        if (verboseDeviceLogs)
            Debug.Log($"[PoseSource] XR device config changed: name={device.name}, role={device.role}, chars={device.characteristics}");

        RefreshDevices();
    }

    private float GetNow()
    {
        return Time.unscaledTime;
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