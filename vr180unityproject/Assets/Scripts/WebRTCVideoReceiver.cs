using System;
using System.Linq;
using System.Collections;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Networking;

public class WebRTCVideoReceiver : MonoBehaviour
{
    [Header("Signaling")]
    public string signalingBaseUrl = "http://127.0.0.1:8080";

    [Header("Video Output")]
    public Renderer targetRenderer;
    public string textureProperty = "_MainTex";

    private RTCPeerConnection pc;
    private MediaStream remoteStream;

    private Texture remoteTexture;

    IEnumerator Start()
    {
        Application.targetFrameRate = 120;
        QualitySettings.vSyncCount = 0;
        StartCoroutine(WebRTC.Update());

        if (targetRenderer == null)
        {
            Debug.LogError("Target Renderer is not assigned.");
            yield break;
        }

        var config = default(RTCConfiguration);
        pc = new RTCPeerConnection(ref config);

        pc.OnTrack = e =>
        {
            Debug.Log($"OnTrack: kind={e.Track.Kind}");

            if (e.Track is VideoStreamTrack videoTrack)
            {
                remoteStream ??= new MediaStream();
                remoteStream.AddTrack(videoTrack);

                videoTrack.OnVideoReceived += tex =>
                {
                    remoteTexture = tex;
                    targetRenderer.material.SetTexture(textureProperty, remoteTexture);
                };
            }
        };

        // Receive-only transceiver
        var transceiver = pc.AddTransceiver(TrackKind.Video, new RTCRtpTransceiverInit
        {
            direction = RTCRtpTransceiverDirection.RecvOnly
        });

        // var codecs = RTCRtpReceiver.GetCapabilities(TrackKind.Video).codecs;
        // var h264 = codecs.Where(c => c.mimeType == "video/H264").ToArray();
        // var err = transceiver.SetCodecPreferences(h264);
        // if (err != RTCErrorType.None)
        // {
        //     Debug.LogWarning($"SetCodecPreferences error: {err}");
        // }

        // Create offer
        var op = pc.CreateOffer();
        yield return op;

        if (op.IsError)
        {
            Debug.LogError($"CreateOffer error: {op.Error.message}");
            yield break;
        }

        var offerDesc = op.Desc;

        var setLocalOp = pc.SetLocalDescription(ref offerDesc);
        yield return setLocalOp;

        if (setLocalOp.IsError)
        {
            Debug.LogError($"SetLocalDescription error: {setLocalOp.Error.message}");
            yield break;
        }

        // Send offer to your signaling server
        string offerSdp = offerDesc.sdp;
        yield return StartCoroutine(PostOfferAndSetAnswer(offerSdp));
    }

    IEnumerator PostOfferAndSetAnswer(string offerSdp)
    {
        var reqBody = JsonUtility.ToJson(new SdpMessage
        {
            type = "offer",
            sdp = offerSdp
        });

        using var req = new UnityWebRequest(signalingBaseUrl + "/offer", "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(reqBody);
        req.uploadHandler = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Signaling POST failed: " + req.error);
            yield break;
        }

        var answer = JsonUtility.FromJson<SdpMessage>(req.downloadHandler.text);

        var answerDesc = new RTCSessionDescription
        {
            type = RTCSdpType.Answer,
            sdp = answer.sdp
        };

        var op = pc.SetRemoteDescription(ref answerDesc);
        yield return op;

        if (op.IsError)
        {
            Debug.LogError($"SetRemoteDescription error: {op.Error.message}");
            yield break;
        }

        Debug.Log("WebRTC remote answer applied.");
    }

    private void OnDestroy()
    {
        remoteTexture = null;

        if (remoteStream != null)
        {
            foreach (var track in remoteStream.GetTracks())
                track.Dispose();

            remoteStream.Dispose();
            remoteStream = null;
        }

        if (pc != null)
        {
            pc.Close();
            pc.Dispose();
            pc = null;
        }
    }

    [Serializable]
    private class SdpMessage
    {
        public string type;
        public string sdp;
    }
}