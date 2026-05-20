using System.Collections.Generic;
using UnityEngine;

public class MockTwistFovMaskController : MonoBehaviour
{
    [Header("Follow")]
    public Transform headsetTransform;

    [Header("Aperture")]
    [Range(10f, 170f)]
    public float horizontalFovDeg = 70f;
    public float aspect = 16f / 9f;

    [Header("Sphere Mask")]
    public MeshFilter sphereMaskMeshFilter;
    public float sphereRadius = 20f;
    [Range(24, 256)]
    public int longitudeSegments = 128;
    [Range(12, 128)]
    public int latitudeSegments = 64;
    public bool frontHemisphereOnly = true;

    private Mesh runtimeSphereMesh;
    private float lastHorizontalFovDeg = -1f;
    private float lastAspect = -1f;
    private float lastSphereRadius = -1f;
    private int lastLongitudeSegments = -1;
    private int lastLatitudeSegments = -1;
    private bool lastFrontHemisphereOnly;

    void LateUpdate()
    {
        Transform head = headsetTransform;
        if (head == null && Camera.main != null)
            head = Camera.main.transform;

        if (head == null)
            return;

        transform.rotation = head.rotation;

        if (sphereMaskMeshFilter == null)
            return;

        sphereMaskMeshFilter.transform.localPosition = Vector3.zero;
        sphereMaskMeshFilter.transform.localRotation = Quaternion.identity;
        sphereMaskMeshFilter.transform.localScale = Vector3.one;

        if (NeedsSphereRebuild())
            RebuildSphereMaskMesh();
    }

    private bool NeedsSphereRebuild()
    {
        return runtimeSphereMesh == null
               || !Mathf.Approximately(lastHorizontalFovDeg, horizontalFovDeg)
               || !Mathf.Approximately(lastAspect, aspect)
               || !Mathf.Approximately(lastSphereRadius, sphereRadius)
               || lastLongitudeSegments != longitudeSegments
               || lastLatitudeSegments != latitudeSegments
               || lastFrontHemisphereOnly != frontHemisphereOnly;
    }

    private void RebuildSphereMaskMesh()
    {
        if (sphereMaskMeshFilter == null)
            return;

        if (runtimeSphereMesh == null)
        {
            runtimeSphereMesh = new Mesh();
            runtimeSphereMesh.name = "MockTwistMaskSphereRuntime";
            sphereMaskMeshFilter.sharedMesh = runtimeSphereMesh;
        }
        else
        {
            runtimeSphereMesh.Clear();
        }

        float safeAspect = Mathf.Max(aspect, 0.001f);
        float halfH = 0.5f * Mathf.Deg2Rad * Mathf.Clamp(horizontalFovDeg, 1f, 179f);
        float halfV = Mathf.Atan(Mathf.Tan(halfH) / safeAspect);

        int lon = Mathf.Max(8, longitudeSegments);
        int lat = Mathf.Max(4, latitudeSegments);
        float radius = Mathf.Max(0.05f, sphereRadius);

        int vertCount = (lat + 1) * (lon + 1);
        Vector3[] vertices = new Vector3[vertCount];
        Vector3[] normals = new Vector3[vertCount];
        Vector2[] uv = new Vector2[vertCount];

        for (int iy = 0; iy <= lat; iy++)
        {
            float v = (float)iy / lat;
            float elevation = Mathf.Lerp(-0.5f * Mathf.PI, 0.5f * Mathf.PI, v);
            float cosElev = Mathf.Cos(elevation);
            float y = Mathf.Sin(elevation);

            for (int ix = 0; ix <= lon; ix++)
            {
                float u = (float)ix / lon;
                float azimuth = Mathf.Lerp(-Mathf.PI, Mathf.PI, u);

                float x = cosElev * Mathf.Sin(azimuth);
                float z = cosElev * Mathf.Cos(azimuth);
                Vector3 dir = new Vector3(x, y, z);

                int idx = iy * (lon + 1) + ix;
                vertices[idx] = dir * radius;
                normals[idx] = -dir; // inward-facing
                uv[idx] = new Vector2(u, v);
            }
        }

        List<int> triangles = new List<int>(lon * lat * 6);

        for (int iy = 0; iy < lat; iy++)
        {
            for (int ix = 0; ix < lon; ix++)
            {
                int a = iy * (lon + 1) + ix;
                int b = a + 1;
                int c = a + (lon + 1);
                int d = c + 1;

                AddMaskedTriangle(vertices[a], vertices[d], vertices[b], a, d, b, halfH, halfV, triangles, radius);
                AddMaskedTriangle(vertices[a], vertices[c], vertices[d], a, c, d, halfH, halfV, triangles, radius);
            }
        }

        runtimeSphereMesh.vertices = vertices;
        runtimeSphereMesh.normals = normals;
        runtimeSphereMesh.uv = uv;
        runtimeSphereMesh.SetTriangles(triangles, 0);
        runtimeSphereMesh.RecalculateBounds();

        lastHorizontalFovDeg = horizontalFovDeg;
        lastAspect = aspect;
        lastSphereRadius = sphereRadius;
        lastLongitudeSegments = lon;
        lastLatitudeSegments = lat;
        lastFrontHemisphereOnly = frontHemisphereOnly;
    }

    private void AddMaskedTriangle(
        Vector3 va,
        Vector3 vb,
        Vector3 vc,
        int ia,
        int ib,
        int ic,
        float halfH,
        float halfV,
        List<int> triangles,
        float radius)
    {
        Vector3 centroid = (va + vb + vc) / (3f * Mathf.Max(radius, 0.0001f));
        if (!ShouldRenderMaskAtDirection(centroid.normalized, halfH, halfV))
            return;

        // Keep clockwise order from inside so front-face culling does not remove mask.
        triangles.Add(ia);
        triangles.Add(ib);
        triangles.Add(ic);
    }

    private bool ShouldRenderMaskAtDirection(Vector3 dir, float halfH, float halfV)
    {
        if (frontHemisphereOnly && dir.z <= 0f)
            return false;

        if (dir.z <= 0f)
            return true;

        float azimuth = Mathf.Abs(Mathf.Atan2(dir.x, dir.z));
        float elevation = Mathf.Abs(Mathf.Atan2(dir.y, dir.z));
        bool insideWindow = azimuth <= halfH && elevation <= halfV;
        return !insideWindow;
    }

    private void OnDisable()
    {
        if (runtimeSphereMesh != null)
        {
            Destroy(runtimeSphereMesh);
            runtimeSphereMesh = null;
        }
    }
}
