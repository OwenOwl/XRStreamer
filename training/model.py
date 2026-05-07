"""
TorsoTransformer — Transformer encoder for torso orientation prediction.

Input:  (B, T, FEATURE_DIM)
Output: (B, T, TARGET_DIM)   (predicts at every frame; use last frame for causal inference)
"""

from __future__ import annotations
import math

import torch
import torch.nn as nn

from .dataset import FEATURE_DIM, TARGET_DIM


class SinusoidalPositionalEncoding(nn.Module):
    def __init__(self, d_model: int, max_len: int = 2048, dropout: float = 0.1):
        super().__init__()
        self.dropout = nn.Dropout(dropout)
        pe = torch.zeros(max_len, d_model)
        pos = torch.arange(max_len).unsqueeze(1)
        div = torch.exp(torch.arange(0, d_model, 2) * (-math.log(10000.0) / d_model))
        pe[:, 0::2] = torch.sin(pos * div)
        pe[:, 1::2] = torch.cos(pos * div)
        self.register_buffer("pe", pe.unsqueeze(0))  # (1, max_len, d_model)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        x = x + self.pe[:, : x.size(1)]
        return self.dropout(x)


class TorsoTransformer(nn.Module):
    """
    Args:
        feat_dim:    input feature dimension (default FEATURE_DIM=19).
        target_dim:  output dimension (default TARGET_DIM=4).
        d_model:     transformer hidden size.
        nhead:       number of attention heads.
        num_layers:  number of encoder layers.
        dim_ff:      feedforward hidden size.
        dropout:     dropout probability.
        causal:      if True, uses a causal (autoregressive) attention mask.
    """

    def __init__(
        self,
        feat_dim:   int = FEATURE_DIM,
        target_dim: int = TARGET_DIM,
        d_model:    int = 128,
        nhead:      int = 4,
        num_layers: int = 3,
        dim_ff:     int = 256,
        dropout:    float = 0.1,
        causal:     bool = True,
    ):
        super().__init__()
        self.causal = causal

        self.input_proj = nn.Linear(feat_dim, d_model)
        self.pos_enc    = SinusoidalPositionalEncoding(d_model, dropout=dropout)

        encoder_layer = nn.TransformerEncoderLayer(
            d_model=d_model,
            nhead=nhead,
            dim_feedforward=dim_ff,
            dropout=dropout,
            batch_first=True,
            norm_first=True,   # pre-norm — more stable
        )
        self.encoder = nn.TransformerEncoder(encoder_layer, num_layers=num_layers)
        self.head     = nn.Linear(d_model, target_dim)

    def _causal_mask(self, T: int, device: torch.device) -> torch.Tensor:
        """Upper-triangular mask (additive, -inf above diagonal)."""
        mask = torch.triu(torch.full((T, T), float("-inf"), device=device), diagonal=1)
        return mask

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        """
        Args:
            x: (B, T, feat_dim)

        Returns:
            (B, T, target_dim)
        """
        x = self.input_proj(x)
        x = self.pos_enc(x)

        mask = self._causal_mask(x.size(1), x.device) if self.causal else None
        x = self.encoder(x, mask=mask)
        return self.head(x)
