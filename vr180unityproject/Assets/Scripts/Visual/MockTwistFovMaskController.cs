using UnityEngine;

public class MockTwistFovMaskController : MonoBehaviour
{
    [Header("Follow")]
    public Transform headsetTransform;
    public bool followPosition = false;
    public Vector3 localOffset = Vector3.zero;

    [Header("Aperture")]
    [Range(10f, 170f)]
    public float horizontalFovDeg = 70f;
    public float aspect = 16f / 9f;
    public float maskDistance = 0.8f;

    [Header("Planes")]
    public Transform topPlane;
    public Transform bottomPlane;
    public Transform leftPlane;
    public Transform rightPlane;
    public float planeDepth = 0.02f;
    public float planeExtent = 4f;

    void LateUpdate()
    {
        Transform head = headsetTransform;
        if (head == null && Camera.main != null)
            head = Camera.main.transform;

        if (head == null)
            return;

        transform.rotation = head.rotation;
        if (followPosition)
            transform.position = head.position;

        transform.position += transform.rotation * localOffset;

        UpdatePlanes();
    }

    private void UpdatePlanes()
    {
        float halfHFovRad = 0.5f * horizontalFovDeg * Mathf.Deg2Rad;
        float windowHalfWidth = Mathf.Tan(halfHFovRad) * maskDistance;
        float windowHalfHeight = windowHalfWidth / Mathf.Max(aspect, 0.001f);

        // Top / Bottom cover the area outside the visible window on Y axis.
        PlaceHorizontalPlane(topPlane, windowHalfHeight + planeDepth * 0.5f, planeExtent, planeDepth);
        PlaceHorizontalPlane(bottomPlane, -(windowHalfHeight + planeDepth * 0.5f), planeExtent, planeDepth);

        // Left / Right cover the area outside the visible window on X axis.
        PlaceVerticalPlane(leftPlane, -(windowHalfWidth + planeDepth * 0.5f), planeDepth, 2f * windowHalfHeight + 2f * planeDepth);
        PlaceVerticalPlane(rightPlane, windowHalfWidth + planeDepth * 0.5f, planeDepth, 2f * windowHalfHeight + 2f * planeDepth);
    }

    private void PlaceHorizontalPlane(Transform plane, float y, float width, float height)
    {
        if (plane == null)
            return;

        plane.localPosition = new Vector3(0f, y, maskDistance);
        plane.localRotation = Quaternion.identity;
        plane.localScale = new Vector3(width, height, 1f);
    }

    private void PlaceVerticalPlane(Transform plane, float x, float width, float height)
    {
        if (plane == null)
            return;

        plane.localPosition = new Vector3(x, 0f, maskDistance);
        plane.localRotation = Quaternion.identity;
        plane.localScale = new Vector3(width, height, 1f);
    }
}
