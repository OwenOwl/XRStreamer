using UnityEngine;

public class PoseSource : MonoBehaviour
{
    [Header("Device Refresh")]
    public float refreshInterval = 1.0f;
    public bool verboseDeviceLogs = false;

    [Header("Status")]
    public bool ok = false;
    public bool okHmd = false;
    public bool okLeft = false;
    public bool okRight = false;

    [Header("HMD Pose")]
    public Vector3 hmdPos = Vector3.zero;
    public Quaternion hmdQuat = Quaternion.identity;

    [Header("Left Controller")]
    public Vector3 leftPos = Vector3.zero;
    public Quaternion leftQuat = Quaternion.identity;
    public Vector2 leftStick = Vector2.zero;
    public float leftTrigger = 0f;
    public float leftGrip = 0f;
    public int leftPrimaryButton = 0;
    public int leftSecondaryButton = 0;
    public int leftStickClick = 0;

    [Header("Right Controller")]
    public Vector3 rightPos = Vector3.zero;
    public Quaternion rightQuat = Quaternion.identity;
    public Vector2 rightStick = Vector2.zero;
    public float rightTrigger = 0f;
    public float rightGrip = 0f;
    public int rightPrimaryButton = 0;
    public int rightSecondaryButton = 0;
    public int rightStickClick = 0;
}