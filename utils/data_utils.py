import torch

from .axis_utils import (
	pos_unity_to_genesis,
	quat_apply,
	quat_conjugate,
	quat_from_yaw,
	quat_to_rpy,
	quat_unity_to_genesis,
)


def _ema_smooth_sequence(x: torch.Tensor, alpha: float) -> torch.Tensor:
	"""
	EMA smoothing over time axis (dim=0):
		y[t] = alpha * x[t] + (1 - alpha) * y[t-1]
	"""
	if x.ndim < 1:
		raise ValueError(f"Expected at least 1D tensor, got {x.shape}")
	if not (0.0 < alpha <= 1.0):
		raise ValueError(f"Expected alpha in (0, 1], got {alpha}")

	y = torch.empty_like(x)
	y[0] = x[0]
	for t in range(1, x.shape[0]):
		y[t] = alpha * x[t] + (1.0 - alpha) * y[t - 1]
	return y


def _compute_velocity_and_speed_ema(
	signal: torch.Tensor,
	time: torch.Tensor,
	ema_alpha: float,
	eps: float = 1e-6,
) -> torch.Tensor:
	"""
	Compute first-order time derivative from signal/time and EMA-smooth it.

	Args:
		signal: (T, D)
		time: (T,)

	Returns:
		vel_ema: (T, D)
	"""
	if signal.ndim != 2:
		raise ValueError(f"Expected signal as (T, D), got {signal.shape}")
	if time.ndim != 1 or time.shape[0] != signal.shape[0]:
		raise ValueError(f"Expected time as (T,), got {time.shape} for T={signal.shape[0]}")

	vel = torch.zeros_like(signal)
	dt = torch.clamp(time[1:] - time[:-1], min=eps)
	vel[1:] = (signal[1:] - signal[:-1]) / dt[:, None]

	vel_ema = _ema_smooth_sequence(vel, alpha=ema_alpha)
	return vel_ema


def process_pose_sequence_unity_to_genesis(data: torch.Tensor) -> torch.Tensor:
	"""
	Expected row layout from PoseSequenceRecorder:
		[time,
		 hmd_px,hmd_py,hmd_pz,hmd_qx,hmd_qy,hmd_qz,hmd_qw,
		 left_px,left_py,left_pz,left_qx,left_qy,left_qz,left_qw,
		 right_px,right_py,right_pz,right_qx,right_qy,right_qz,right_qw,
		 imu_r,imu_p,imu_y,
		 left_trigger]

	Args:
		data: (T, 26) tensor.

	Returns:
		(T, 26) tensor with HMD, left-hand, and right-hand poses converted
		from Unity coordinates to Genesis coordinates. Time, IMU Euler angles,
		and left trigger are preserved.
	"""
	if data.ndim != 2 or data.shape[-1] != 26:
		raise ValueError(f"Expected (T, 26), got {tuple(data.shape)}")

	converted = data.clone()

	pose_starts = (1, 8, 15)
	for start in pose_starts:
		pos = converted[:, start : start + 3]
		quat = converted[:, start + 3 : start + 7]
		converted[:, start : start + 3] = pos_unity_to_genesis(pos)
		converted[:, start + 3 : start + 7] = quat_unity_to_genesis(quat)

	return converted


def build_pose_training_data(data: torch.Tensor, ema_alpha: float = 0.2) -> dict[str, object]:
	"""
	Create a training-oriented view of the recorded pose sequence.

	Args:
		data: (T, 26) tensor in the PoseSequenceRecorder layout.
		ema_alpha: EMA coefficient for velocity/speed smoothing.

	Returns:
		A dict containing:
			pose_sequence:
				(T, 26) tensor after Unity-to-Genesis pose conversion.
			features:
				dict of extra per-frame features for model training.
				Currently includes:
					- hmd_height: (T, 1) HMD z in Genesis space.
					- hmd_roll: (T, 1) HMD roll in radians.
					- hmd_pitch: (T, 1) HMD pitch in radians.
					- hmd_disp_from_start_yaw_inv: (T, 3) displacement from frame 0
					  expressed in current-frame yaw-stabilized HMD frame.
					- hmd_yaw_vel_ema: (T, 1) EMA-smoothed HMD yaw velocity in rad/s.
					- left_rel_pos_yaw_inv: (T, 3) left relative pos in yaw-stabilized frame.
					- right_rel_pos_yaw_inv: (T, 3) right relative pos in yaw-stabilized frame.
					- left_rel_vel_yaw_inv_ema: (T, 3) EMA-smoothed left relative velocity
					  in yaw-stabilized HMD frame.
					- right_rel_vel_yaw_inv_ema: (T, 3) EMA-smoothed right relative velocity
					  in yaw-stabilized HMD frame.
			targets:
				dict of supervision targets.
				Currently includes:
					- imu_rpy: (T, 3) IMU roll/pitch/yaw where yaw is first-frame
					  aligned and then made relative to current HMD yaw.
	"""
	pose_sequence = process_pose_sequence_unity_to_genesis(data[:, :26])  # (T, 26)

	hmd_pos = pose_sequence[:, 1:4]
	hmd_quat = pose_sequence[:, 4:8]
	left_pos = pose_sequence[:, 8:11]
	right_pos = pose_sequence[:, 15:18]

	hmd_roll, hmd_pitch, hmd_yaw = quat_to_rpy(hmd_quat)
	hmd_yaw_inv = quat_conjugate(quat_from_yaw(hmd_yaw))
	hmd_disp_from_start_yaw_inv = quat_apply(hmd_yaw_inv, hmd_pos - hmd_pos[0:1])

	left_rel_pos_yaw_inv = quat_apply(hmd_yaw_inv, left_pos - hmd_pos)
	right_rel_pos_yaw_inv = quat_apply(hmd_yaw_inv, right_pos - hmd_pos)

	time = pose_sequence[:, 0]
	hmd_yaw_vel_ema = _compute_velocity_and_speed_ema(hmd_yaw[:, None], time, ema_alpha)
	left_rel_vel_yaw_inv_ema = _compute_velocity_and_speed_ema(left_rel_pos_yaw_inv, time, ema_alpha)
	right_rel_vel_yaw_inv_ema = _compute_velocity_and_speed_ema(right_rel_pos_yaw_inv, time, ema_alpha)

	features = {
		"hmd_height": hmd_pos[:, 2:3],
		"hmd_roll": hmd_roll[:, None],
		"hmd_pitch": hmd_pitch[:, None],
		"hmd_disp_from_start_yaw_inv": hmd_disp_from_start_yaw_inv,
		"hmd_yaw_vel_ema": hmd_yaw_vel_ema,
		"left_rel_pos_yaw_inv": left_rel_pos_yaw_inv,
		"right_rel_pos_yaw_inv": right_rel_pos_yaw_inv,
		"left_rel_vel_yaw_inv_ema": left_rel_vel_yaw_inv_ema,
		"right_rel_vel_yaw_inv_ema": right_rel_vel_yaw_inv_ema,
	}
	imu_rpy = pose_sequence[:, 22:25].clone()
	imu_rpy_cur = imu_rpy[:] - imu_rpy[0]
	hmd_yaw_deg = torch.rad2deg(hmd_yaw)
	hmd_yaw_deg_cur = hmd_yaw_deg - hmd_yaw_deg[0]
	imu_rpy_cur[:, 2] = imu_rpy_cur[:, 2] - hmd_yaw_deg_cur

	targets = {
		"imu_rpy": imu_rpy_cur,
	}

	return {
		"pose_sequence": pose_sequence,
		"features": features,
		"targets": targets,
	}
