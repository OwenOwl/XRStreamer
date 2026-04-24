using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class VRStartupPanel : MonoBehaviour
{
    [Header("UI - Left")]
    public Toggle domeToggle;
    public TMP_InputField cameraInput;
    public TMP_InputField cameraImuPortInput;

    public Toggle bodyImuToggle;
    public TMP_InputField bodyImuPortInput;

    public TMP_InputField targetIpInput;

    public Button startStreamingButtonXR;

    [Header("UI - Right")]
    public Button startRecordingButton;

    [Header("Sources")]
    public IMUSource[] imuSources;
    public PoseSource[] poseSources;

    [Header("Targets")]
    public DomeStabilizer domeStabilizer;
    public OBSReceiver obsReceiver;
    public PoseUdpSender poseUdpSender;
    public PoseSequenceRecorder poseSequenceRecorder;

    [Header("Remove Visuals")]
    public GameObject StreamerRoot;
    public GameObject recorderRoot;
    public GameObject[] XRVisuals;

    [Header("Default Text Values")]
    public string defaultCameraImuPortName = "COM7";
    public string defaultBodyImuPortName = "COM3";
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
        
        if (poseSources != null)
        {
            foreach (var source in poseSources)
            {
                if (source != null)
                    source.enabled = false;
            }
        }

        if (imuSources != null)
        {
            foreach (var source in imuSources)
            {
                if (source != null)
                    source.enabled = false;
            }
        }

        if (domeStabilizer != null)
            domeStabilizer.enabled = false;

        if (obsReceiver != null)
            obsReceiver.enabled = false;

        if (poseUdpSender != null)
            poseUdpSender.enabled = false;

        if (poseSequenceRecorder != null)
            poseSequenceRecorder.enabled = false;

        if (StreamerRoot != null)
            StreamerRoot.SetActive(false);

        if (recorderRoot != null)
            recorderRoot.SetActive(false);
    }

    private void InitInputDefaults()
    {
        if (cameraInput != null && string.IsNullOrWhiteSpace(cameraInput.text))
            cameraInput.text = defaultCameraName;

        if (targetIpInput != null && string.IsNullOrWhiteSpace(targetIpInput.text))
            targetIpInput.text = defaultTargetIp;

        if (cameraImuPortInput != null && string.IsNullOrWhiteSpace(cameraImuPortInput.text))
            cameraImuPortInput.text = defaultCameraImuPortName;

        if (bodyImuPortInput != null && string.IsNullOrWhiteSpace(bodyImuPortInput.text))
            bodyImuPortInput.text = defaultBodyImuPortName;
    }

    private string GetCameraImuPortName()
    {
        if (cameraImuPortInput == null || string.IsNullOrWhiteSpace(cameraImuPortInput.text))
            return defaultCameraImuPortName;

        return cameraImuPortInput.text.Trim();
    }

    private string GetBodyImuPortName()
    {
        if (bodyImuPortInput == null || string.IsNullOrWhiteSpace(bodyImuPortInput.text))
            return defaultBodyImuPortName;

        return bodyImuPortInput.text.Trim();
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

        if (domeToggle != null && domeToggle.isOn && domeStabilizer != null)
        {
            domeStabilizer.imuSource.portName = GetCameraImuPortName();
            domeStabilizer.imuSource.enabled = true;
            domeStabilizer.imuSource.OpenPort();
            domeStabilizer.enabled = true;
        }

        if (domeToggle != null && domeToggle.isOn && obsReceiver != null)
        {
            obsReceiver.cameraName = GetCameraName();
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
            poseUdpSender.targetIp = GetTargetIp();
            poseUdpSender.poseSource = selectedPoseSource;
            poseUdpSender.enabled = true;
            poseUdpSender.BeginStreaming();
        }

        if (poseSequenceRecorder != null && bodyImuToggle != null && bodyImuToggle.isOn)
        {
            poseUdpSender.imuSource.portName = GetBodyImuPortName();
            poseUdpSender.imuSource.enabled = true;
            poseUdpSender.imuSource.OpenPort();
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

        PoseSource selectedPoseSource = null;
        if (poseSources != null)
        {
            selectedPoseSource = poseSources[0];
            if (selectedPoseSource != null)
                selectedPoseSource.enabled = true;
        }

        if (poseSequenceRecorder != null)
        {
            poseSequenceRecorder.poseSource = selectedPoseSource;
            poseSequenceRecorder.enabled = true;
            poseSequenceRecorder.InitializeRecorder();
        }

        if (poseSequenceRecorder != null && bodyImuToggle != null && bodyImuToggle.isOn)
        {
            poseSequenceRecorder.imuSource.portName = GetBodyImuPortName();
            poseSequenceRecorder.imuSource.enabled = true;
            poseSequenceRecorder.imuSource.OpenPort();
        }
    }
}