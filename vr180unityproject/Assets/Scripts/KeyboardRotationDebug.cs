using UnityEngine;

public class KeyboardRotationDebug : MonoBehaviour
{
    public DomeStabilizer stabilizer;
    public float yawSpeed = 60f;
    public float pitchSpeed = 60f;
    public float rollSpeed = 60f;

    private Quaternion debugRotation = Quaternion.identity;

    void Update()
    {
        if (stabilizer == null) return;

        float yaw = 0f;
        float pitch = 0f;
        float roll = 0f;

        // Yaw
        if (Input.GetKey(KeyCode.A))    yaw -= yawSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.D))    yaw += yawSpeed * Time.deltaTime;

        // Pitch
        if (Input.GetKey(KeyCode.W))    pitch -= pitchSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.S))    pitch += pitchSpeed * Time.deltaTime;

        // Roll
        if (Input.GetKey(KeyCode.Q))    roll -= rollSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.E))    roll += rollSpeed * Time.deltaTime;

        Quaternion delta = Quaternion.Euler(pitch, yaw, roll);
        debugRotation = delta * debugRotation;

        stabilizer.SetRobotRotation(debugRotation);

        // Reset
        if (Input.GetKeyDown(KeyCode.R))
        {
            debugRotation = Quaternion.identity;
            stabilizer.SetRobotRotation(debugRotation);
        }
    }
}