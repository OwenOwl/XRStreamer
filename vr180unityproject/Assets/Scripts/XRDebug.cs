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

        float p  = mat.GetFloat("_ProjectionScale");

        if (rightHand.TryGetFeatureValue(CommonUsages.triggerButton, out bool rightTriggerPressed) && rightTriggerPressed)
        {
            p -= 0.02f * Time.deltaTime;
        }
        if (rightHand.TryGetFeatureValue(CommonUsages.gripButton, out bool rightGripPressed) && rightGripPressed)
        {
            p += 0.02f * Time.deltaTime;
        }

        mat.SetFloat("_ProjectionScale", p);
    }
}