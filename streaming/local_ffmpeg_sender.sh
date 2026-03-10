ffmpeg -f dshow -i video="VR.Cam 02" \
  -c:v libx264 -preset ultrafast -tune zerolatency -g 15 -bf 0 -pix_fmt yuv420p \
  -f mpegts udp://127.0.0.1:5000