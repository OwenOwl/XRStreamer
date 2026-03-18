import argparse
import asyncio
import json

import aiohttp
import cv2
from aiortc import RTCPeerConnection, RTCSessionDescription
from aiortc.rtcrtpsender import RTCRtpSender


def force_h264(pc: RTCPeerConnection) -> None:
    """
    Prefer H.264 on all video transceivers.
    """
    capabilities = RTCRtpSender.getCapabilities("video")
    if capabilities is None:
        return

    h264_codecs = [
        c for c in capabilities.codecs
        if c.mimeType.lower() == "video/h264"
    ]
    if not h264_codecs:
        raise RuntimeError("No H.264 codec available in aiortc/FFmpeg environment.")

    for transceiver in pc.getTransceivers():
        if transceiver.kind == "video":
            transceiver.setCodecPreferences(h264_codecs)


async def run(server_url: str) -> None:
    pc = RTCPeerConnection()

    @pc.on("track")
    def on_track(track):
        print("Received track:", track.kind)

        if track.kind == "video":
            asyncio.create_task(display_video(track))

        @track.on("ended")
        async def on_ended():
            print("Track ended")

    # Receive-only transceiver
    pc.addTransceiver("video", direction="recvonly")

    # Force codec preference to H.264 before creating offer
    # force_h264(pc)

    offer = await pc.createOffer()
    await pc.setLocalDescription(offer)

    async with aiohttp.ClientSession() as session:
        async with session.post(
            f"{server_url.rstrip('/')}/offer",
            json={
                "sdp": pc.localDescription.sdp,
                "type": pc.localDescription.type,
            },
        ) as resp:
            resp.raise_for_status()
            answer = await resp.json()

    await pc.setRemoteDescription(
        RTCSessionDescription(sdp=answer["sdp"], type=answer["type"])
    )

    print("Connected. Press q in the video window to quit.")

    try:
        while True:
            await asyncio.sleep(1)
    except KeyboardInterrupt:
        pass
    finally:
        await pc.close()
        cv2.destroyAllWindows()


async def display_video(track) -> None:
    """
    Receive decoded frames from aiortc and display them using OpenCV.
    """
    window_name = "WebRTC H264 Receiver"
    cv2.namedWindow(window_name, cv2.WINDOW_NORMAL)

    while True:
        try:
            frame = await track.recv()
        except Exception as e:
            print("Video receive ended:", e)
            break

        img = frame.to_ndarray(format="bgr24")
        cv2.imshow(window_name, img)

        # q to quit
        if cv2.waitKey(1) & 0xFF == ord("q"):
            break

    cv2.destroyWindow(window_name)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--server", default="http://127.0.0.1:8080")
    args = parser.parse_args()

    asyncio.run(run(args.server))


if __name__ == "__main__":
    main()