"""
Real-time inference for torso IMU prediction.

Input per frame (21D, Genesis coordinates):
    [time,
     hmd_px, hmd_py, hmd_pz, hmd_qx, hmd_qy, hmd_qz, hmd_qw,
     left_px, left_py, left_pz, left_qx, left_qy, left_qz, left_qw,
     right_px, right_py, right_pz, right_qx, right_qy, right_qz, right_qw]

Output per frame:
    Predicted IMU RPY in HMD yaw coordinate frame (degrees):
    [imu_roll_deg, imu_pitch_deg, imu_yaw_deg]
"""

from __future__ import annotations

from collections import deque
from pathlib import Path

import torch

from .training.model import TorsoTransformer

from .utils.axis_utils import (
    quat_apply,
    quat_conjugate,
    quat_from_yaw,
    quat_to_rpy,
)


class OnlinePoseFeatureBuilder:
    """Builds the same 19D feature vector used during training, one frame at a time."""

    def __init__(self, ema_alpha: float = 0.2, eps: float = 1e-6, device: str | torch.device = "cpu"):
        self.ema_alpha = float(ema_alpha)
        self.eps = float(eps)
        self.device = torch.device(device)
        self.reset()

    def reset(self) -> None:
        self.start_hmd_pos: torch.Tensor | None = None
        self.prev_time: torch.Tensor | None = None
        self.prev_hmd_yaw: torch.Tensor | None = None
        self.prev_left_rel: torch.Tensor | None = None
        self.prev_right_rel: torch.Tensor | None = None

        self.hmd_yaw_vel_ema = torch.zeros(1, device=self.device)
        self.left_rel_vel_ema = torch.zeros(3, device=self.device)
        self.right_rel_vel_ema = torch.zeros(3, device=self.device)

    def _update_ema_vel(self, cur: torch.Tensor, prev: torch.Tensor | None, dt: torch.Tensor, prev_ema: torch.Tensor) -> torch.Tensor:
        if prev is None:
            inst = torch.zeros_like(cur)
        else:
            inst = (cur - prev) / dt
        return self.ema_alpha * inst + (1.0 - self.ema_alpha) * prev_ema

    def step(self, frame_21d: torch.Tensor, frame_time: float) -> torch.Tensor:
        """
        Args:
            frame_21d: shape (21,), Genesis coordinates.
            frame_time: timestamp for this frame in seconds.

        Returns:
            Feature vector of shape (19) in Genesis coordinates.
        """
        frame = torch.as_tensor(frame_21d, dtype=torch.float32, device=self.device)
        if frame.ndim != 1 or frame.numel() != 21:
            raise ValueError(f"Expected input shape (21,), got {tuple(frame.shape)}")

        t = torch.tensor(frame_time, dtype=torch.float32, device=self.device)
        hmd_pos = frame[0:3]
        hmd_quat = frame[3:7]
        left_pos = frame[7:10]
        right_pos = frame[14:17]

        hmd_roll, hmd_pitch, hmd_yaw = quat_to_rpy(hmd_quat[None, :])
        hmd_roll = hmd_roll[0]
        hmd_pitch = hmd_pitch[0]
        hmd_yaw = hmd_yaw[0]

        hmd_yaw_inv = quat_conjugate(quat_from_yaw(hmd_yaw[None]))[0]

        if self.start_hmd_pos is None:
            self.start_hmd_pos = hmd_pos.clone()

        hmd_disp_from_start = hmd_pos - self.start_hmd_pos
        hmd_disp_from_start_yaw_inv = quat_apply(hmd_yaw_inv[None, :], hmd_disp_from_start[None, :])[0]

        left_rel = left_pos - hmd_pos
        right_rel = right_pos - hmd_pos
        left_rel_yaw_inv = quat_apply(hmd_yaw_inv[None, :], left_rel[None, :])[0]
        right_rel_yaw_inv = quat_apply(hmd_yaw_inv[None, :], right_rel[None, :])[0]

        if self.prev_time is None:
            dt = torch.tensor(1.0, dtype=torch.float32, device=self.device)
        else:
            dt = torch.clamp(t - self.prev_time, min=self.eps)

        self.hmd_yaw_vel_ema = self._update_ema_vel(hmd_yaw[None], self.prev_hmd_yaw[None] if self.prev_hmd_yaw is not None else None, dt, self.hmd_yaw_vel_ema)
        self.left_rel_vel_ema = self._update_ema_vel(left_rel_yaw_inv, self.prev_left_rel, dt, self.left_rel_vel_ema)
        self.right_rel_vel_ema = self._update_ema_vel(right_rel_yaw_inv, self.prev_right_rel, dt, self.right_rel_vel_ema)

        self.prev_time = t
        self.prev_hmd_yaw = hmd_yaw
        self.prev_left_rel = left_rel_yaw_inv
        self.prev_right_rel = right_rel_yaw_inv

        feat = torch.cat(
            [
                hmd_pos[2:3],
                hmd_roll[None],
                hmd_pitch[None],
                hmd_disp_from_start_yaw_inv,
                self.hmd_yaw_vel_ema,
                left_rel_yaw_inv,
                right_rel_yaw_inv,
                self.left_rel_vel_ema,
                self.right_rel_vel_ema,
            ],
            dim=0,
        )
        return feat


class RealTimeIMUPredictor:
    """
    Streaming predictor for IMU RPY in HMD yaw frame.

    Notes:
    - Model output follows training target convention:
      [roll_deg, pitch_deg, sin(yaw_rad), cos(yaw_rad)]
    - Returned yaw is decoded with atan2(sin, cos) in degrees.
    """

    def __init__(
        self,
        checkpoint_path: str | Path = Path(__file__).parent / "checkpoints/checkpoint.ckpt",
        window: int = 32,
        ema_alpha: float = 0.2,
        device: str | torch.device = "cpu",
        normalizer_path: str | Path | None = None,
        model_kwargs: dict | None = None,
    ):
        self.device = torch.device(device)
        self.window = int(window)
        self.model = self._load_model(checkpoint_path, model_kwargs=model_kwargs)
        self.model.to(self.device)
        self.model.eval()

        self.feature_builder = OnlinePoseFeatureBuilder(ema_alpha=ema_alpha, device=self.device)
        self.feature_buffer: deque[torch.Tensor] = deque(maxlen=self.window)
        self._last_time: float | None = None
        self._last_result: dict[str, torch.Tensor] | None = None

        self.norm_mean: torch.Tensor | None = None
        self.norm_std: torch.Tensor | None = None
        if normalizer_path is not None:
            normalizer = torch.load(str(normalizer_path), map_location="cpu")
            self.norm_mean = normalizer["mean"].to(self.device).float()
            self.norm_std = normalizer["std"].to(self.device).float().clamp(min=1e-6)

    def _load_model(self, checkpoint_path: str | Path, model_kwargs: dict | None = None) -> TorsoTransformer:
        model = TorsoTransformer(**(model_kwargs or {}))
        ckpt = torch.load(str(checkpoint_path), map_location="cpu")

        if isinstance(ckpt, dict) and "state_dict" in ckpt:
            raw_sd = ckpt["state_dict"]
        elif isinstance(ckpt, dict):
            raw_sd = ckpt
        else:
            raise ValueError("Unsupported checkpoint format")

        # Lightning checkpoint usually prefixes model params with "model.".
        if any(k.startswith("model.") for k in raw_sd.keys()):
            state_dict = {k[len("model."):]: v for k, v in raw_sd.items() if k.startswith("model.")}
        else:
            state_dict = raw_sd

        model.load_state_dict(state_dict)
        return model

    def reset(self) -> None:
        self.feature_builder.reset()
        self.feature_buffer.clear()
        self._last_time = None
        self._last_result = None

    def _make_window_tensor(self) -> torch.Tensor:
        if not self.feature_buffer:
            raise RuntimeError("Feature buffer is empty")

        x = torch.stack(list(self.feature_buffer), dim=0)  # (N, 19)
        if x.shape[0] < self.window:
            pad = x[0:1].repeat(self.window - x.shape[0], 1)
            x = torch.cat([pad, x], dim=0)

        if self.norm_mean is not None and self.norm_std is not None:
            x = (x - self.norm_mean) / self.norm_std

        return x.unsqueeze(0)  # (1, W, 19)

    @torch.no_grad()
    def predict(self, frame_21d: torch.Tensor, frame_time: float) -> dict[str, torch.Tensor]:
        """
        Push one 21D frame and get the current prediction.

        Args:
            frame_21d: (21,) Genesis-space frame.
            frame_time: timestamp for this frame in seconds.

        Returns:
            dict with:
              - imu_rpy_hmd_yaw_frame_deg: (3,) tensor [roll, pitch, yaw]
              - raw_head_output: (4,) tensor [roll, pitch, sin_yaw, cos_yaw]
        """
        frame = torch.as_tensor(frame_21d, dtype=torch.float32, device=self.device)
        if frame.ndim != 1 or frame.numel() != 21:
            raise ValueError(f"Expected input shape (21,), got {tuple(frame.shape)}")

        if self._last_time is not None and frame_time == self._last_time:
            if self._last_result is None:
                raise RuntimeError("Cached frame time exists without cached prediction")
            return {
                "imu_rpy_hmd_yaw_frame_deg": self._last_result["imu_rpy_hmd_yaw_frame_deg"].clone(),
                "raw_head_output": self._last_result["raw_head_output"].clone(),
            }

        feat = self.feature_builder.step(frame, frame_time)
        self.feature_buffer.append(feat)

        x = self._make_window_tensor().to(self.device)
        pred = self.model(x)[0, -1]  # last frame output, shape (4,)

        roll_deg = pred[0]
        pitch_deg = pred[1]
        yaw_deg = torch.rad2deg(torch.atan2(pred[2], pred[3]))

        imu_rpy = torch.stack([roll_deg, pitch_deg, yaw_deg], dim=0)
        result = {
            "imu_rpy_hmd_yaw_frame_deg": imu_rpy,
            "raw_head_output": pred,
        }
        self._last_time = frame_time
        self._last_result = {
            "imu_rpy_hmd_yaw_frame_deg": imu_rpy.clone(),
            "raw_head_output": pred.clone(),
        }
        return result

    @torch.no_grad()
    def predict_imu_rpy(self, frame_21d: torch.Tensor, frame_time: float) -> torch.Tensor:
        return self.predict(frame_21d, frame_time)["imu_rpy_hmd_yaw_frame_deg"]
