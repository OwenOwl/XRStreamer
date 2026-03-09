using UnityEngine;
using UnityEngine.XR;

public class XRDebug : MonoBehaviour
{
    public Material mat;

    private InputDevice leftHand;
    private InputDevice rightHand;

    void Start()
    {
        TryInitializeDevices();
    }

    void TryInitializeDevices()
    {
        leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    void Update()
    {
        if (!leftHand.isValid || !rightHand.isValid)
            TryInitializeDevices();

        float cx = mat.GetFloat("_CenterX");
        float cy = mat.GetFloat("_CenterY");
        float r  = mat.GetFloat("_Radius");
        float fov = mat.GetFloat("_FovDeg");

        if (leftHand.TryGetFeatureValue(CommonUsages.triggerButton, out bool leftTriggerPressed) && leftTriggerPressed)
        {
            fov -= 40f * Time.deltaTime;
        }
        if (leftHand.TryGetFeatureValue(CommonUsages.gripButton, out bool leftGripPressed) && leftGripPressed)
        {
            fov += 40f * Time.deltaTime;
        }

        if (rightHand.TryGetFeatureValue(CommonUsages.triggerButton, out bool rightTriggerPressed) && rightTriggerPressed)
        {
            r -= 0.2f * Time.deltaTime;
        }
        if (rightHand.TryGetFeatureValue(CommonUsages.gripButton, out bool rightGripPressed) && rightGripPressed)
        {
            r += 0.2f * Time.deltaTime;
        }

        mat.SetFloat("_CenterX", cx);
        mat.SetFloat("_CenterY", cy);
        mat.SetFloat("_Radius", r);
        mat.SetFloat("_FovDeg", fov);
    }
}