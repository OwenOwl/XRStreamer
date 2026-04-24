using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;

public class DomeStabilizer : MonoBehaviour
{
    [Header("IMU")]
    public IMUSource imuSource;

    [Header("IMU Delay")]
    public float imuDelaySeconds = 0.0f;

    [Header("Debug / Inspector")]
    public Quaternion robotRotation = Quaternion.identity;

    private bool hasReferenceRotation = false;
    private Quaternion referenceRotation = Quaternion.identity;

    void OnEnable()
    {
        
    }

    void OnDisable()
    {
        
    }

    void Start()
    {
        
    }

    void Update()
    {
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