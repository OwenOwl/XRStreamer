using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;

public class DomeStabilizer : MonoBehaviour
{
    public enum StabilizationMode
    {
        ImuYawStabilized,
        FollowHeadset
    }

    [Header("IMU")]
    public IMUSource imuSource;

    [Header("Mode")]
    public StabilizationMode mode = StabilizationMode.ImuYawStabilized;
    public Transform headsetTransform;

    [Header("IMU Delay")]
    public float imuDelaySeconds = 0.0f;

    [Header("Debug / Inspector")]
    public Quaternion robotRotation = Quaternion.identity;

    private bool hasReferenceRotation = false;
    private Quaternion referenceRotation = Quaternion.identity;

    void OnEnable()
    {
        hasReferenceRotation = false;
        referenceRotation = Quaternion.identity;
    }

    void OnDisable()
    {
        
    }

    void Start()
    {
        
    }

    void Update()
    {
        if (mode == StabilizationMode.FollowHeadset)
        {
            UpdateFollowHeadset();
            return;
        }

        UpdateImuYawStabilized();
    }

    public void SetFollowHeadsetMode(bool enable)
    {
        mode = enable ? StabilizationMode.FollowHeadset : StabilizationMode.ImuYawStabilized;
    }

    private void UpdateFollowHeadset()
    {
        Transform head = headsetTransform;
        if (head == null && Camera.main != null)
            head = Camera.main.transform;

        if (head == null)
            return;

        robotRotation = head.rotation;
        transform.rotation = robotRotation;
    }

    private void UpdateImuYawStabilized()
    {
        if (imuSource == null)
            return;

        if (!hasReferenceRotation && imuSource.HasData())
        {
            float yaw = imuSource.GetEulerDeg().z; // original format: x=roll y=pitch z=yaw
            Quaternion yaw_only = Quaternion.AngleAxis(yaw, -Vector3.up);
            referenceRotation = yaw_only;
            hasReferenceRotation = true;
        }

        double targetTime = imuSource.clock.Elapsed.TotalSeconds - imuDelaySeconds;
        Quaternion imuRotation = imuSource.GetImuRotation(targetTime);
        robotRotation = Quaternion.Inverse(referenceRotation) * imuRotation;

        transform.localRotation = robotRotation;
    }
}