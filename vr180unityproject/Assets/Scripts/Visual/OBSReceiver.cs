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

    private WebCamTexture cam;
    private bool started = false;

    void OnEnable()
    {
        
    }    

    void OnDisable()
    {
        StopReceiver();
    }

    void Start()
    {
        
    }

    public void StartReceiver()
    {
        if (started)
            return;

        try {
            cam = new WebCamTexture(cameraName, width, height, fps);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OBSReceiver] Failed to start camera '{cameraName}': {ex.Message}");
            return;
        }

        if (targetRenderer != null)
            targetRenderer.material.mainTexture = cam;

        try {
            cam.Play();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OBSReceiver] Failed to play camera '{cameraName}': {ex.Message}");
            cam = null;
            return;
        }

        started = true;

        Debug.Log($"[OBSReceiver] Started camera: {cameraName}");
    }

    public void StopReceiver()
    {
        if (cam != null)
        {
            if (cam.isPlaying)
                cam.Stop();

            cam = null;
        }

        started = false;
    }

    public static string[] GetAvailableCameraNames()
    {
        var devices = WebCamTexture.devices;
        string[] names = new string[devices.Length];
        for (int i = 0; i < devices.Length; i++)
            names[i] = devices[i].name;
        return names;
    }
}