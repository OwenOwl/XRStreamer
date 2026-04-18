using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class VRStartupPanel : MonoBehaviour
{
    [Header("UI - Left")]
    public Toggle domeToggle;
    public TMP_InputField cameraInput;
    public TMP_InputField portInput;

    public TMP_InputField targetIpInput;

    public Button startStreamingButtonXR;

    [Header("UI - Right")]
    public Button startRecordingButton;

    [Header("Targets")]
    public DomeStabilizer domeStabilizer;
    public OBSReceiver obsReceiver;
    public PoseUdpSender poseUdpSender;
    public PoseSource[] poseSources;
    public PoseSequenceRecorder poseSequenceRecorder;

    [Header("Remove Visuals")]
    public GameObject StreamerRoot;
    public GameObject recorderRoot;
    public GameObject[] XRVisuals;

    [Header("Default Text Values")]
    public string defaultPortName = "COM3";
    public string defaultCameraName = "OBS Virtual Camera";
    public string defaultTargetIp = "255.255.255.255";

    private bool streamingStarted = false;
    private bool recorderStarted = false;

    void Start()
    {
        InitInputDefaults();

        if (startStreamingButtonXR != null)
            startStreamingButtonXR.onClick.AddListener(OnClickStartStreamingXR);

        if (startRecordingButton != null)
            startRecordingButton.onClick.AddListener(OnClickStartRecording);

        if (domeStabilizer != null)
            domeStabilizer.enabled = false;

        if (obsReceiver != null)
            obsReceiver.enabled = false;

        if (poseUdpSender != null)
            poseUdpSender.enabled = false;
        
        if (poseSources != null)
        {
            foreach (var source in poseSources)
            {
                if (source != null)
                    source.enabled = false;
            }
        }

        if (poseSequenceRecorder != null)
            poseSequenceRecorder.enabled = false;

        if (StreamerRoot != null)
            StreamerRoot.SetActive(false);

        if (recorderRoot != null)
            recorderRoot.SetActive(false);
    }

    private void InitInputDefaults()
    {
        if (portInput != null && string.IsNullOrWhiteSpace(portInput.text))
            portInput.text = defaultPortName;

        if (cameraInput != null && string.IsNullOrWhiteSpace(cameraInput.text))
            cameraInput.text = defaultCameraName;

        if (targetIpInput != null && string.IsNullOrWhiteSpace(targetIpInput.text))
            targetIpInput.text = defaultTargetIp;
    }

    private string GetPortName()
    {
        if (portInput == null || string.IsNullOrWhiteSpace(portInput.text))
            return defaultPortName;

        return portInput.text.Trim();
    }

    private string GetCameraName()
    {
        if (cameraInput == null || string.IsNullOrWhiteSpace(cameraInput.text))
            return defaultCameraName;

        return cameraInput.text.Trim();
    }

    private string GetTargetIp()
    {
        if (targetIpInput == null || string.IsNullOrWhiteSpace(targetIpInput.text))
            return defaultTargetIp;

        return targetIpInput.text.Trim();
    }

    public void RemoveVRVisuals()
    {
        if (XRVisuals != null)
        {
            foreach (var visual in XRVisuals)
            {
                if (visual != null)
                    visual.SetActive(false);
            }
        }
    }

    public void OnClickStartStreaming(int sourceIndex = -1)
    {
        RemoveVRVisuals();

        if (streamingStarted) return;
        streamingStarted = true;

        if (StreamerRoot != null)
            StreamerRoot.SetActive(true);

        string selectedPort = GetPortName();
        string selectedCamera = GetCameraName();
        string targetIp = GetTargetIp();

        if (domeToggle != null && domeToggle.isOn && domeStabilizer != null)
        {
            domeStabilizer.portName = selectedPort;
            domeStabilizer.enabled = true;
            domeStabilizer.OpenPort();
        }

        if (domeToggle != null && domeToggle.isOn && obsReceiver != null)
        {
            obsReceiver.cameraName = selectedCamera;
            obsReceiver.enabled = true;
            obsReceiver.StartReceiver();
        }

        PoseSource selectedPoseSource = null;
        if (poseSources != null && sourceIndex >= 0 && sourceIndex < poseSources.Length)
        {
            selectedPoseSource = poseSources[sourceIndex];
            if (selectedPoseSource != null)
                selectedPoseSource.enabled = true;
        }

        if (poseUdpSender != null && selectedPoseSource != null)
        {
            poseUdpSender.targetIp = targetIp;
            poseUdpSender.poseSource = selectedPoseSource;
            poseUdpSender.enabled = true;
            poseUdpSender.BeginStreaming();
        }
    }

    public void OnClickStartStreamingXR()
    {
        OnClickStartStreaming(0);
    }

    public void OnClickStartRecording()
    {
        RemoveVRVisuals();

        if (recorderStarted) return;
        recorderStarted = true;

        if (recorderRoot != null)
            recorderRoot.SetActive(true);

        if (poseSequenceRecorder != null)
        {
            poseSequenceRecorder.enabled = true;
            poseSequenceRecorder.portName = GetPortName();
            poseSequenceRecorder.InitializeRecorder();
        }

        gameObject.SetActive(false);

        if (XRVisuals != null)
        {
            foreach (var visual in XRVisuals)
            {
                if (visual != null)
                    visual.SetActive(false);
            }
        }
    }
}