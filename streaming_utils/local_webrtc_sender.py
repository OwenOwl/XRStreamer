import argparse
import asyncio
import json
import os
from aiohttp import web

from aiortc import RTCPeerConnection, RTCSessionDescription
from aiortc.contrib.media import MediaPlayer

pcs = set()

HTML = "ok"

def create_local_video_track(device_name: str, width: int, height: int, fps: int, os_name: str):
    if os_name == "windows":
        # DirectShow camera name, e.g. "USB Video"
        options = {
            "video_size": f"{width}x{height}",
            "framerate": str(fps),
            # latency-related
            "fflags": "nobuffer",
            "flags": "low_delay",
            "avioflags": "direct",
            "probesize": "32",
            "analyzeduration": "0",
        }
        player = MediaPlayer(device_name, format="dshow", options=options)
    elif os_name == "linux":
        # device_name should be like /dev/video0
        options = {
            "video_size": f"{width}x{height}",
            "framerate": str(fps),
        }
        player = MediaPlayer(device_name, format="v4l2", options=options)
    else:
        raise ValueError("Unsupported os_name. Use 'windows' or 'linux'.")

    if player.video is None:
        raise RuntimeError("Could not open camera video track.")
    return player, player.video

async def index(request):
    return web.Response(text=HTML)

async def offer(request):
    params = await request.json()
    offer = RTCSessionDescription(sdp=params["sdp"], type=params["type"])

    pc = RTCPeerConnection()
    pcs.add(pc)

    # Open the camera for this peer connection
    player, video_track = create_local_video_track(
        device_name=request.app["device_name"],
        width=request.app["width"],
        height=request.app["height"],
        fps=request.app["fps"],
        os_name=request.app["os_name"],
    )

    request.app["players"].append(player)

    @pc.on("connectionstatechange")
    async def on_connectionstatechange():
        print("Connection state:", pc.connectionState)
        if pc.connectionState in ("failed", "closed", "disconnected"):
            await pc.close()
            pcs.discard(pc)

    pc.addTrack(video_track)

    await pc.setRemoteDescription(offer)

    answer = await pc.createAnswer()
    await pc.setLocalDescription(answer)

    return web.json_response(
        {
            "sdp": pc.localDescription.sdp,
            "type": pc.localDescription.type,
        }
    )

async def on_shutdown(app):
    coros = [pc.close() for pc in pcs]
    await asyncio.gather(*coros, return_exceptions=True)
    pcs.clear()

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--device", required=True, help='Camera device name. Windows example: "USB Video". Linux example: /dev/video0')
    parser.add_argument("--os", required=True, choices=["windows", "linux"], help="Sender OS")
    parser.add_argument("--width", type=int, default=3840)
    parser.add_argument("--height", type=int, default=1920)
    parser.add_argument("--fps", type=int, default=30)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8080)
    args = parser.parse_args()

    app = web.Application()
    app["device_name"] = args.device
    app["os_name"] = args.os
    app["width"] = args.width
    app["height"] = args.height
    app["fps"] = args.fps
    app["players"] = []

    app.on_shutdown.append(on_shutdown)
    app.router.add_get("/", index)
    app.router.add_post("/offer", offer)

    print(f"Starting signaling server at http://{args.host}:{args.port}")
    print(f"Using camera: {args.device}")
    web.run_app(app, host=args.host, port=args.port)

if __name__ == "__main__":
    main()