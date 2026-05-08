"""
PoseSequenceDataset — sliding-window dataset built on top of build_pose_training_data.

Feature vector (per frame, dim=19):
    hmd_height              (1)
    hmd_roll                (1)
    hmd_pitch               (1)
    hmd_disp_from_start_yaw_inv (3)
    hmd_yaw_vel_ema         (1)
    left_rel_pos_yaw_inv    (3)
    right_rel_pos_yaw_inv   (3)
    left_rel_vel_yaw_inv_ema  (3)
    right_rel_vel_yaw_inv_ema (3)

Target vector (per frame, dim=4):
    imu_roll                (1)  degrees
    imu_pitch               (1)  degrees
    sin(imu_yaw_rad)        (1)
    cos(imu_yaw_rad)        (1)
"""

from __future__ import annotations

import math
from pathlib import Path

import numpy as np
import torch
from torch.utils.data import Dataset

try:
    from utils.data_utils import build_pose_training_data
except ImportError:
    from ..utils.data_utils import build_pose_training_data

# Feature columns in order
FEATURE_KEYS = [
    "hmd_height",
    "hmd_roll",
    "hmd_pitch",
    "hmd_disp_from_start_yaw_inv",
    "hmd_yaw_vel_ema",
    "left_rel_pos_yaw_inv",
    "right_rel_pos_yaw_inv",
    "left_rel_vel_yaw_inv_ema",
    "right_rel_vel_yaw_inv_ema",
]

FEATURE_DIM = 19   # sum of widths above
TARGET_DIM  = 4    # roll, pitch, sin(yaw), cos(yaw)


def _load_npy_sequence(path: str | Path) -> torch.Tensor:
    arr = np.load(str(path))
    return torch.from_numpy(arr).float()


def _build_features_targets(data: torch.Tensor, ema_alpha: float) -> tuple[torch.Tensor, torch.Tensor]:
    """Return (T, FEATURE_DIM) and (T, TARGET_DIM)."""
    out = build_pose_training_data(data, ema_alpha=ema_alpha)
    features_dict = out["features"]
    targets_dict  = out["targets"]

    feat = torch.cat([features_dict[k] for k in FEATURE_KEYS], dim=-1)  # (T, 19)

    imu_rpy = targets_dict["imu_rpy"]  # (T, 3) in degrees
    roll_deg  = imu_rpy[:, 0:1]
    pitch_deg = imu_rpy[:, 1:2]
    yaw_rad   = torch.deg2rad(imu_rpy[:, 2:3])
    tgt = torch.cat([roll_deg, pitch_deg, torch.sin(yaw_rad), torch.cos(yaw_rad)], dim=-1)  # (T, 4)

    return feat, tgt


class PoseSequenceDataset(Dataset):
    """
    Sliding-window dataset over one or more .npy sequence files.

    Args:
        npy_paths: list of paths to .npy files (each shape (T, 26)).
        window:    number of consecutive frames per sample.
        stride:    step between window start positions.
        ema_alpha: passed through to build_pose_training_data.
    """

    def __init__(
        self,
        npy_paths: list[str | Path],
        window: int = 32,
        stride: int = 1,
        ema_alpha: float = 0.2,
    ):
        self.window = window
        self.stride = stride

        # Build per-sequence tensors and collect sliding-window index pointers
        self._feats: list[torch.Tensor] = []
        self._tgts:  list[torch.Tensor] = []
        self._windows: list[tuple[int, int]] = []  # (seq_idx, start_frame)

        for path in npy_paths:
            raw = _load_npy_sequence(path)
            feat, tgt = _build_features_targets(raw, ema_alpha)
            T = feat.shape[0]
            seq_idx = len(self._feats)
            self._feats.append(feat)
            self._tgts.append(tgt)
            for start in range(0, T - window + 1, stride):
                self._windows.append((seq_idx, start))

    def __len__(self) -> int:
        return len(self._windows)

    def __getitem__(self, idx: int) -> tuple[torch.Tensor, torch.Tensor]:
        seq_idx, start = self._windows[idx]
        end = start + self.window
        x = self._feats[seq_idx][start:end]   # (W, 19)
        y = self._tgts[seq_idx][start:end]    # (W, 4)
        return x, y


class RunningNormalizer:
    """
    Fit mean/std over training data and apply z-score normalisation.
    Can be saved/loaded with torch.save / torch.load.
    """

    def __init__(self):
        self.mean: torch.Tensor | None = None
        self.std:  torch.Tensor | None = None

    def fit(self, dataset: PoseSequenceDataset) -> "RunningNormalizer":
        all_feats = torch.cat([f for f in dataset._feats], dim=0)
        self.mean = all_feats.mean(0)
        self.std  = all_feats.std(0).clamp(min=1e-6)
        return self

    def transform(self, x: torch.Tensor) -> torch.Tensor:
        return (x - self.mean.to(x.device)) / self.std.to(x.device)

    def state_dict(self) -> dict:
        return {"mean": self.mean, "std": self.std}

    def load_state_dict(self, d: dict) -> None:
        self.mean = d["mean"]
        self.std  = d["std"]
