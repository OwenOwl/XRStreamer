using UnityEngine;

public class OBSReceiver : MonoBehaviour
{
    [Header("Input")]
    public string cameraName = "OBS Virtual Camera";
    public int width = 3840;
    public int height = 1920;
    public int fps = 30;

    [Header("Video Output")]
    public Renderer targetRenderer;

    WebCamTexture cam;

    void Start()
    {
        cam = new WebCamTexture(cameraName, width, height, fps);

        if (targetRenderer != null)
        {
            targetRenderer.material.mainTexture = cam;
        }

        cam.Play();
    }
}