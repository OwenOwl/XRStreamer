using UnityEngine;

public class FisheyeMaterialDebug : MonoBehaviour
{
    public Material mat;

    void Update()
    {
        if (mat == null) return;

        float cx = mat.GetFloat("_CenterX");
        float cy = mat.GetFloat("_CenterY");
        float r  = mat.GetFloat("_Radius");
        float fov = mat.GetFloat("_FovDeg");

        if (Input.GetKey(KeyCode.Z)) r -= 0.2f * Time.deltaTime;
        if (Input.GetKey(KeyCode.X)) r += 0.2f * Time.deltaTime;

        if (Input.GetKey(KeyCode.C)) fov -= 40f * Time.deltaTime;
        if (Input.GetKey(KeyCode.V)) fov += 40f * Time.deltaTime;

        mat.SetFloat("_CenterX", cx);
        mat.SetFloat("_CenterY", cy);
        mat.SetFloat("_Radius", r);
        mat.SetFloat("_FovDeg", fov);
    }
}