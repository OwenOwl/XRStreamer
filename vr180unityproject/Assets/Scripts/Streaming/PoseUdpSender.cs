using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class PoseUdpSender : MonoBehaviour
{
    [Header("Source")]
    public PoseSource poseSource;

    [Header("UDP")]
    public string targetIp = "255.255.255.255";
    public int targetPort = 5005;
    public bool enableBroadcast = true;

    [Header("Streaming")]
    public int targetHz = 120;
    public bool useUnscaledTime = true;

    [Header("Status")]
    public bool isStreaming = false;

    private UdpClient udp;
    private IPEndPoint remoteEndPoint;

    private ulong frameId = 0;
    private float sendInterval;
    private float nextSendTime;

    void OnDisable()
    {
        StopStreaming();
    }

    public void BeginStreaming()
    {
        if (isStreaming)
            return;

        if (poseSource == null)
        {
            Debug.LogError("[PoseUdpSender] poseSource is not assigned.");
            return;
        }

        sendInterval = 1.0f / Mathf.Max(1, targetHz);
        nextSendTime = GetNow();

        try
        {
            udp = new UdpClient();
            udp.EnableBroadcast = enableBroadcast;
            remoteEndPoint = new IPEndPoint(IPAddress.Parse(targetIp), targetPort);

            Debug.Log($"[PoseUdpSender] UDP -> {targetIp}:{targetPort}, broadcast={enableBroadcast}, targetHz={targetHz}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[PoseUdpSender] UDP init failed: {e}");
            return;
        }

        isStreaming = true;
    }

    public void StopStreaming()
    {
        isStreaming = false;

        if (udp != null)
        {
            udp.Close();
            udp = null;
        }
    }

    void Update()
    {
        if (!isStreaming)
            return;

        if (poseSource == null)
            return;

        float now = GetNow();

        if (now < nextSendTime)
            return;

        nextSendTime += sendInterval;
        if (now > nextSendTime + 0.5f)
            nextSendTime = now + sendInterval;

        if (!poseSource.ok)
            return;

        frameId++;

        string msg = string.Format(
            CultureInfo.InvariantCulture,
            "FRAME,{0}," +
            "HMD,{1},{2},{3},{4},{5},{6},{7}," +
            "LEFTHAND,{8},{9},{10},{11},{12},{13},{14}," +
            "LEFTSTICK,{15},{16}," +
            "LEFTTRIGGER,{17}," +
            "LEFTGRIP,{18}," +
            "LEFTKEYS,{19},{20},{21}," +
            "RIGHTHAND,{22},{23},{24},{25},{26},{27},{28}," +
            "RIGHTSTICK,{29},{30}," +
            "RIGHTTRIGGER,{31}," +
            "RIGHTGRIP,{32}," +
            "RIGHTKEYS,{33},{34},{35}",
            frameId,

            poseSource.hmdPos.x, poseSource.hmdPos.y, poseSource.hmdPos.z,
            poseSource.hmdQuat.x, poseSource.hmdQuat.y, poseSource.hmdQuat.z, poseSource.hmdQuat.w,

            poseSource.leftPos.x, poseSource.leftPos.y, poseSource.leftPos.z,
            poseSource.leftQuat.x, poseSource.leftQuat.y, poseSource.leftQuat.z, poseSource.leftQuat.w,
            poseSource.leftStick.x, poseSource.leftStick.y,
            poseSource.leftTrigger,
            poseSource.leftGrip,
            poseSource.leftPrimaryButton, poseSource.leftSecondaryButton, poseSource.leftStickClick,

            poseSource.rightPos.x, poseSource.rightPos.y, poseSource.rightPos.z,
            poseSource.rightQuat.x, poseSource.rightQuat.y, poseSource.rightQuat.z, poseSource.rightQuat.w,
            poseSource.rightStick.x, poseSource.rightStick.y,
            poseSource.rightTrigger,
            poseSource.rightGrip,
            poseSource.rightPrimaryButton, poseSource.rightSecondaryButton, poseSource.rightStickClick
        );

        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(msg);
            udp.Send(bytes, bytes.Length, remoteEndPoint);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PoseUdpSender] UDP send failed: {e.Message}");
        }
    }

    private float GetNow()
    {
        return useUnscaledTime ? Time.unscaledTime : Time.time;
    }
}