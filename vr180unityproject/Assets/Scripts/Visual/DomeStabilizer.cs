using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Mathematics;
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
    public float mockTwistDelaySeconds = 0.1f;

    [Header("Debug / Inspector")]
    public Quaternion robotRotation = Quaternion.identity;

    private bool hasReferenceRotation = false;
    private Quaternion referenceRotation = Quaternion.identity;

    void OnEnable()
    {
        hasReferenceRotation = false;
        referenceRotation = Quaternion.identity;

        clock.Start();
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

    public readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();

    private struct TimestampedRotation
    {
        public double time;
        public Quaternion rotation;
    }
    private readonly Queue<TimestampedRotation> rotationBuffer = new Queue<TimestampedRotation>();

    private void UpdateFollowHeadset()
    {
        // imuSource gives body rotation
        if (imuSource == null)
            return;
        
        Transform head = headsetTransform;
        if (head == null && Camera.main != null)
            head = Camera.main.transform;

        if (head == null)
            return;
        
        if (!hasReferenceRotation)
        {
            float bodyYawInit = -imuSource.GetEulerDeg().z; // r handed to l handed
            float headYawInit = head.eulerAngles.y;
            float yawOffset = Mathf.DeltaAngle(bodyYawInit, headYawInit);
            referenceRotation = Quaternion.AngleAxis(yawOffset, Vector3.up);
            hasReferenceRotation = true;
        }

        // relative rotation from body to head
        float bodyYaw = -imuSource.GetEulerDeg().z;
        Quaternion bodyRotation = Quaternion.AngleAxis(bodyYaw, Vector3.up);
        Quaternion referenceRotationCurrent = referenceRotation * bodyRotation;
        Quaternion currentHeadRotation = Quaternion.Inverse(referenceRotationCurrent) * head.rotation;
        // get a delayed looking direction to mock twist neck delay
        rotationBuffer.Enqueue(new TimestampedRotation { time = clock.Elapsed.TotalSeconds, rotation = currentHeadRotation });
        while (rotationBuffer.Count > 1 && rotationBuffer.Peek().time <= clock.Elapsed.TotalSeconds - mockTwistDelaySeconds)
            rotationBuffer.Dequeue();
        Quaternion delayedlookingRotation = rotationBuffer.Peek().rotation;
        // set the doom
        robotRotation = currentHeadRotation * Quaternion.Inverse(delayedlookingRotation);

        transform.rotation = robotRotation;
    }

    private void UpdateImuYawStabilized()
    {
        // imuSource gives camera rotation
        if (imuSource == null)
            return;

        if (!hasReferenceRotation && imuSource.HasData())
        {
            // original format: x=roll y=pitch z=yaw
            float yaw = -imuSource.GetEulerDeg().z; // r handed to l handed
            referenceRotation = Quaternion.AngleAxis(yaw, Vector3.up);;
            hasReferenceRotation = true;
        }

        double targetTime = imuSource.clock.Elapsed.TotalSeconds - imuDelaySeconds;
        Quaternion imuRotation = imuSource.GetImuRotation(targetTime);
        // Unity uses ZXY extrinsic
        robotRotation = Quaternion.Inverse(referenceRotation) * imuRotation;

        transform.localRotation = robotRotation;
    }
}