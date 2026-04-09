import torch


# Quaternion convention in this file:
# q = (..., 4) with components [x, y, z, w]


def quat_normalize(q: torch.Tensor, eps: float = 1e-8) -> torch.Tensor:
    return q / torch.clamp(torch.linalg.norm(q, dim=-1, keepdim=True), min=eps)


def quat_to_matrix(q: torch.Tensor) -> torch.Tensor:
    """
    Convert quaternion(s) [x, y, z, w] to rotation matrix/matrices.

    Args:
        q: (..., 4)

    Returns:
        (..., 3, 3)
    """
    q = quat_normalize(q)
    x, y, z, w = q.unbind(dim=-1)

    xx = x * x
    yy = y * y
    zz = z * z
    ww = w * w

    xy = x * y
    xz = x * z
    yz = y * z
    xw = x * w
    yw = y * w
    zw = z * w

    m00 = ww + xx - yy - zz
    m01 = 2.0 * (xy - zw)
    m02 = 2.0 * (xz + yw)

    m10 = 2.0 * (xy + zw)
    m11 = ww - xx + yy - zz
    m12 = 2.0 * (yz - xw)

    m20 = 2.0 * (xz - yw)
    m21 = 2.0 * (yz + xw)
    m22 = ww - xx - yy + zz

    return torch.stack(
        [
            torch.stack([m00, m01, m02], dim=-1),
            torch.stack([m10, m11, m12], dim=-1),
            torch.stack([m20, m21, m22], dim=-1),
        ],
        dim=-2,
    )


def matrix_to_quat(m: torch.Tensor, eps: float = 1e-8) -> torch.Tensor:
    """
    Convert rotation matrix/matrices to quaternion(s) [x, y, z, w].

    Args:
        m: (..., 3, 3), should be a proper rotation matrix with det ~= +1

    Returns:
        (..., 4)
    """
    if m.shape[-2:] != (3, 3):
        raise ValueError(f"Expected (..., 3, 3), got {m.shape}")

    m00 = m[..., 0, 0]
    m11 = m[..., 1, 1]
    m22 = m[..., 2, 2]
    trace = m00 + m11 + m22

    q = torch.zeros(m.shape[:-2] + (4,), dtype=m.dtype, device=m.device)

    cond0 = trace > 0
    cond1 = (~cond0) & (m00 >= m11) & (m00 >= m22)
    cond2 = (~cond0) & (~cond1) & (m11 >= m22)
    cond3 = (~cond0) & (~cond1) & (~cond2)

    if cond0.any():
        s = 2.0 * torch.sqrt(torch.clamp(trace[cond0] + 1.0, min=eps))
        q[cond0, 3] = 0.25 * s
        q[cond0, 0] = (m[cond0, 2, 1] - m[cond0, 1, 2]) / s
        q[cond0, 1] = (m[cond0, 0, 2] - m[cond0, 2, 0]) / s
        q[cond0, 2] = (m[cond0, 1, 0] - m[cond0, 0, 1]) / s

    if cond1.any():
        s = 2.0 * torch.sqrt(
            torch.clamp(1.0 + m[cond1, 0, 0] - m[cond1, 1, 1] - m[cond1, 2, 2], min=eps)
        )
        q[cond1, 3] = (m[cond1, 2, 1] - m[cond1, 1, 2]) / s
        q[cond1, 0] = 0.25 * s
        q[cond1, 1] = (m[cond1, 0, 1] + m[cond1, 1, 0]) / s
        q[cond1, 2] = (m[cond1, 0, 2] + m[cond1, 2, 0]) / s

    if cond2.any():
        s = 2.0 * torch.sqrt(
            torch.clamp(1.0 + m[cond2, 1, 1] - m[cond2, 0, 0] - m[cond2, 2, 2], min=eps)
        )
        q[cond2, 3] = (m[cond2, 0, 2] - m[cond2, 2, 0]) / s
        q[cond2, 0] = (m[cond2, 0, 1] + m[cond2, 1, 0]) / s
        q[cond2, 1] = 0.25 * s
        q[cond2, 2] = (m[cond2, 1, 2] + m[cond2, 2, 1]) / s

    if cond3.any():
        s = 2.0 * torch.sqrt(
            torch.clamp(1.0 + m[cond3, 2, 2] - m[cond3, 0, 0] - m[cond3, 1, 1], min=eps)
        )
        q[cond3, 3] = (m[cond3, 1, 0] - m[cond3, 0, 1]) / s
        q[cond3, 0] = (m[cond3, 0, 2] + m[cond3, 2, 0]) / s
        q[cond3, 1] = (m[cond3, 1, 2] + m[cond3, 2, 1]) / s
        q[cond3, 2] = 0.25 * s

    return quat_normalize(q)


def _get_axis_change_matrix(dtype: torch.dtype, device: torch.device) -> torch.Tensor:
    """
    Axis system 1:
        RIGHT-HANDED, x=front, y=left, z=up

    Axis system 2:
        LEFT-HANDED, z=front, x=right, y=up

    Express basis of system 1 in coordinates of system 2:
        x1 ->  z2
        y1 -> -x2
        z1 ->  y2

    So columns are:
        col0 = x1 in sys2 = [ 0, 0, 1]
        col1 = y1 in sys2 = [-1, 0, 0]
        col2 = z1 in sys2 = [ 0, 1, 0]
    """
    return torch.tensor(
        [
            [0.0, -1.0, 0.0],
            [0.0,  0.0, 1.0],
            [1.0,  0.0, 0.0],
        ],
        dtype=dtype,
        device=device,
    )


def quat_genesis_to_unity(q: torch.Tensor) -> torch.Tensor:
    """
    Convert quaternion(s) from axis system 1 to axis system 2.

    Args:
        q: (..., 4) quaternion(s) in system 1, format [x, y, z, w]

    Returns:
        (..., 4) quaternion(s) in system 2, format [x, y, z, w]
    """
    if q.shape[-1] != 4:
        raise ValueError(f"Expected (..., 4), got {q.shape}")

    C = _get_axis_change_matrix(q.dtype, q.device)
    R1 = quat_to_matrix(q)
    R2 = C @ R1 @ C.transpose(-1, -2)
    return matrix_to_quat(R2)


def quat_unity_to_genesis(q: torch.Tensor) -> torch.Tensor:
    """
    Convert quaternion(s) from axis system 2 to axis system 1.

    Args:
        q: (..., 4) quaternion(s) in system 2, format [x, y, z, w]

    Returns:
        (..., 4) quaternion(s) in system 1, format [x, y, z, w]
    """
    if q.shape[-1] != 4:
        raise ValueError(f"Expected (..., 4), got {q.shape}")

    C = _get_axis_change_matrix(q.dtype, q.device)
    R2 = quat_to_matrix(q)
    R1 = C.transpose(-1, -2) @ R2 @ C
    return matrix_to_quat(R1)


def pos_genesis_to_unity(p: torch.Tensor) -> torch.Tensor:
    """
    Convert position from system1 to system2

    Args:
        p: (..., 3)  [x1, y1, z1]

    Returns:
        (..., 3)  [x2, y2, z2]
    """
    if p.shape[-1] != 3:
        raise ValueError(f"Expected (..., 3), got {p.shape}")

    x1, y1, z1 = p.unbind(dim=-1)

    x2 = -y1
    y2 =  z1
    z2 =  x1

    return torch.stack([x2, y2, z2], dim=-1)


def pos_unity_to_genesis(p: torch.Tensor) -> torch.Tensor:
    """
    Convert position from system2 to system1

    Args:
        p: (..., 3)  [x2, y2, z2]

    Returns:
        (..., 3)  [x1, y1, z1]
    """
    if p.shape[-1] != 3:
        raise ValueError(f"Expected (..., 3), got {p.shape}")

    x2, y2, z2 = p.unbind(dim=-1)

    x1 =  z2
    y1 = -x2
    z1 =  y2

    return torch.stack([x1, y1, z1], dim=-1)