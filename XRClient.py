import copy
import socket
import threading
import time
from typing import Any

import torch

from .utils.axis_utils import quat_unity_to_genesis, pos_unity_to_genesis

class XRClient:
    """
    output format (by .get_frame() method):
        frame_id:       int
        recv_time:      float
        link_pos:       torch.tensor (N*3) [HMD, L, R, IMU]
        link_quat:      torch.tensor (N*4) [HMD, L, R, IMU]
        button_states:  dict
            left_stick:     float (2,)
            left_trigger:   float
            left_grip:      float
            left_x:         int
            left_y:         int
            left_click:     int
            right_stick:    float (2,)
            right_trigger:  float
            right_grip:     float
            right_a:        int
            right_b:        int
            right_click:    int
    """
    def __init__(
        self, udp_host: str = "0.0.0.0", udp_port: int = 5005, device: str = "cpu"
    ) -> None:
        self.udp_host = udp_host
        self.udp_port = udp_port

        self._device_str = device
        self.device = torch.device(device)

        self._sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._sock.settimeout(1.0)
        self._sock.bind((udp_host, udp_port))

        self._lock = threading.Lock()
        self._has_frame = threading.Event()
        self._latest_raw: dict[str, Any] = {}

        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._recv_loop, daemon=True)
        
        self.start()

    def start(self) -> None:
        self._thread.start()

    def shutdown(self) -> None:
        self._stop.set()
        try:
            self._sock.close()
        except Exception:
            pass
    
    def get_frame(self) -> dict[str, Any]:
        self._has_frame.wait()
        with self._lock:
            raw = copy.deepcopy(self._latest_raw)
        return raw

    # ---------------- internal ----------------

    def _recv_loop(self) -> None:
        while not self._stop.is_set():
            try:
                data, _ = self._sock.recvfrom(8192)  # blocking recv
            except TimeoutError:
                continue
            except OSError:
                break

            recv_time = time.time()
            msg = data.decode("utf-8", errors="ignore").strip()

            parsed = self._parse_frame_packet_to_lists(msg, recv_time)
            if parsed is None:
                continue

            with self._lock:
                self._latest_raw = parsed
            self._has_frame.set()

    def _parse_frame_packet_to_lists(self, msg: str, recv_time: float) -> dict[str, Any]:
        """
        - Incomming socket format:
            FRAME:          int (1),
            HMD:            float (7),
            LEFTHAND:       float (7),
            LEFTSTICK:      float (2),
            LEFTTRIGGER:    float (1),
            LEFTGRIP:       float (1),
            LEFTKEYS:       int (3),
                - LEFTX
                - LEFTY
                - LEFTCLICK
            RIGHTHAND:      float (7),
            RIGHTSTICK:     float (2),
            RIGHTTRIGGER:   float (1),
            RIGHTGRIP:      float (1),
            RIGHTKEYS:      int (3),
                - RIGHTA
                - RIGHTB
                - RIGHTCLICK
        """
        parts = msg.split(",")
        
        # parse frame id
        frame_id, = self._parse_int_block(parts, "FRAME", 1)

        # parse poses
        hmd_pose  = torch.tensor(
            self._parse_float_block(parts, "HMD", 7),
            dtype=torch.float32,
            device=self.device,
        )
        left_hand_pose = torch.tensor(
            self._parse_float_block(parts, "LEFTHAND", 7),
            dtype=torch.float32,
            device=self.device,
        )
        right_hand_pose = torch.tensor(
            self._parse_float_block(parts, "RIGHTHAND", 7),
            dtype=torch.float32,
            device=self.device,
        )
        body_imu_rot = torch.tensor(
            self._parse_float_block(parts, "BODYIMU", 4),
            dtype=torch.float32,
            device=self.device,
        )

        link_pos = torch.stack(
            [
                hmd_pose[:3],
                left_hand_pose[:3],
                right_hand_pose[:3],
                torch.zeros(3, device=self.device),  # IMU has no position
            ],
            dim=0,
        )
        link_quat = torch.stack(
            [
                hmd_pose[3:],
                left_hand_pose[3:],
                right_hand_pose[3:],
                body_imu_rot,
            ],
            dim=0,
        )
        link_pos = pos_unity_to_genesis(link_pos)
        link_quat = quat_unity_to_genesis(link_quat).roll(1, dims=-1)  # (w, x, y, z)
        
        # parse buttons
        left_stick = tuple(self._parse_float_block(parts, "LEFTSTICK", 2))
        left_trigger, = self._parse_float_block(parts, "LEFTTRIGGER", 1)
        left_grip, = self._parse_float_block(parts, "LEFTGRIP", 1)
        left_x, left_y, left_click, = self._parse_int_block(parts, "LEFTKEYS", 3)
        right_stick = tuple(self._parse_float_block(parts, "RIGHTSTICK", 2))
        right_trigger, = self._parse_float_block(parts, "RIGHTTRIGGER", 1)
        right_grip, = self._parse_float_block(parts, "RIGHTGRIP", 1)
        right_a, right_b, right_click, = self._parse_int_block(parts, "RIGHTKEYS", 3)
        button_states = {
            "left_stick": left_stick,
            "left_trigger": left_trigger,
            "left_grip": left_grip,
            "left_x": left_x,
            "left_y": left_y,
            "left_click": left_click,
            "right_stick": right_stick,
            "right_trigger": right_trigger,
            "right_grip": right_grip,
            "right_a": right_a,
            "right_b": right_b,
            "right_click": right_click,
        }

        return {
            "frame_id": frame_id,
            "recv_time": recv_time,
            "link_pos": link_pos,
            "link_quat": link_quat,
            "button_states": button_states,
        }

    def _parse_int_block(self, parts: list[str], label: str, nums: int) -> list[int]:
        default = [0] * nums
        try:
            i = parts.index(label)
        except ValueError:
            return default
        if i + nums >= len(parts):
            return default
        try:
            x = [int(parts[i + t]) for t in range(1, nums + 1)]
        except ValueError:
            return default
        return x

    def _parse_float_block(self, parts: list[str], label: str, nums: int) -> list[float]:
        default = [0.0] * nums
        try:
            i = parts.index(label)
        except ValueError:
            return default
        if i + nums >= len(parts):
            return default
        try:
            x = [float(parts[i + t]) for t in range(1, nums + 1)]
        except ValueError:
            return default
        return x


if __name__ == "__main__":
    client = XRClient()
    try:
        while True:
            frame = client.get_frame()
            print(frame)
    except KeyboardInterrupt:
        pass
    finally:
        client.shutdown()