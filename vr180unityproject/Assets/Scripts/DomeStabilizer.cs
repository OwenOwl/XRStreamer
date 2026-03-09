using UnityEngine;

public class DomeStabilizer : MonoBehaviour
{
    [Header("Debug / Inspector")]
    public Quaternion robotRotation = Quaternion.identity;

    [Tooltip("Use yaw only for comfort. Usually recommended.")]
    public bool yawOnly = true;

    void Update()
    {
        Quaternion q = robotRotation;

        if (yawOnly)
        {
            Vector3 euler = q.eulerAngles;
            q = Quaternion.Euler(0f, euler.y, 0f);
        }

        // Counter-rotate the dome so the perceived world stays stable
        transform.localRotation = Quaternion.Inverse(q);
    }

    public void SetRobotRotation(Quaternion q)
    {
        robotRotation = q;
    }
}