"""
train.py  —  PyTorch Lightning training script with W&B logging.

Usage:
    python -m training.train \
        --data_dir /path/to/npy_files \
        --val_ratio 0.15 \
        --window 32 \
        --stride 4 \
        --batch_size 256 \
        --max_epochs 100 \
        --d_model 128 \
        --num_layers 3 \
        --lr 3e-4 \
        --run_name my_run

Requires:
    pip install torch lightning wandb
"""

from __future__ import annotations

import argparse
import math
import random
from pathlib import Path

import torch
import torch.nn as nn
import lightning as L
import wandb
from lightning.pytorch.loggers import WandbLogger
from lightning.pytorch.callbacks import ModelCheckpoint, EarlyStopping
from torch.utils.data import DataLoader, Subset

from .dataset import PoseSequenceDataset, RunningNormalizer
from .model import TorsoTransformer


# ---------------------------------------------------------------------------
# Loss
# ---------------------------------------------------------------------------

def torso_loss(
    pred: torch.Tensor,
    tgt: torch.Tensor,
    rp_weight: float = 1.0,
    yaw_weight: float = 1.0,
) -> tuple[torch.Tensor, torch.Tensor, torch.Tensor]:
    """
    pred / tgt: (B, T, 4)  [roll_deg, pitch_deg, sin_yaw, cos_yaw]

    Returns total_loss, rp_loss, yaw_loss.
    """
    rp_loss  = nn.functional.mse_loss(pred[..., :2], tgt[..., :2])
    yaw_loss = nn.functional.mse_loss(pred[..., 2:], tgt[..., 2:])
    total    = rp_weight * rp_loss + yaw_weight * yaw_loss
    return total, rp_loss, yaw_loss


# ---------------------------------------------------------------------------
# LightningModule
# ---------------------------------------------------------------------------

class TorsoLitModule(L.LightningModule):
    def __init__(
        self,
        d_model:    int   = 128,
        nhead:      int   = 4,
        num_layers: int   = 3,
        dim_ff:     int   = 256,
        dropout:    float = 0.1,
        causal:     bool  = True,
        lr:         float = 3e-4,
        weight_decay: float = 1e-4,
        rp_weight:  float = 1.0,
        yaw_weight: float = 1.0,
    ):
        super().__init__()
        self.save_hyperparameters()
        self.model = TorsoTransformer(
            d_model=d_model,
            nhead=nhead,
            num_layers=num_layers,
            dim_ff=dim_ff,
            dropout=dropout,
            causal=causal,
        )
        self.normalizer: RunningNormalizer | None = None

    def set_normalizer(self, normalizer: RunningNormalizer) -> None:
        self.normalizer = normalizer

    def _apply_norm(self, x: torch.Tensor) -> torch.Tensor:
        if self.normalizer is not None:
            return self.normalizer.transform(x)
        return x

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        return self.model(self._apply_norm(x))

    def _shared_step(self, batch, stage: str):
        x, y = batch
        pred = self(x)
        loss, rp_loss, yaw_loss = torso_loss(
            pred, y,
            rp_weight=self.hparams.rp_weight,
            yaw_weight=self.hparams.yaw_weight,
        )
        self.log(f"{stage}/loss",     loss,     prog_bar=True,  sync_dist=True)
        self.log(f"{stage}/rp_loss",  rp_loss,  prog_bar=False, sync_dist=True)
        self.log(f"{stage}/yaw_loss", yaw_loss, prog_bar=False, sync_dist=True)
        return loss

    def training_step(self, batch, _):
        return self._shared_step(batch, "train")

    def validation_step(self, batch, _):
        self._shared_step(batch, "val")

    def configure_optimizers(self):
        opt = torch.optim.AdamW(
            self.parameters(),
            lr=self.hparams.lr,
            weight_decay=self.hparams.weight_decay,
        )
        sched = torch.optim.lr_scheduler.CosineAnnealingLR(
            opt, T_max=self.trainer.max_epochs
        )
        return {"optimizer": opt, "lr_scheduler": {"scheduler": sched, "interval": "epoch"}}


# ---------------------------------------------------------------------------
# CLI entry point
# ---------------------------------------------------------------------------

def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser()
    # Data
    p.add_argument("--data_dir",  required=True, help="Directory containing .npy sequence files")
    p.add_argument("--val_ratio", type=float, default=0.15)
    p.add_argument("--window",    type=int,   default=32)
    p.add_argument("--stride",    type=int,   default=4,  help="Stride between windows during training")
    p.add_argument("--ema_alpha", type=float, default=0.2)
    p.add_argument("--batch_size",type=int,   default=256)
    p.add_argument("--num_workers",type=int,  default=4)
    # Model
    p.add_argument("--d_model",    type=int,   default=128)
    p.add_argument("--nhead",      type=int,   default=4)
    p.add_argument("--num_layers", type=int,   default=3)
    p.add_argument("--dim_ff",     type=int,   default=256)
    p.add_argument("--dropout",    type=float, default=0.1)
    p.add_argument("--causal",     action="store_true", default=True)
    # Training
    p.add_argument("--lr",           type=float, default=3e-4)
    p.add_argument("--weight_decay", type=float, default=1e-4)
    p.add_argument("--max_epochs",   type=int,   default=100)
    p.add_argument("--rp_weight",    type=float, default=1.0)
    p.add_argument("--yaw_weight",   type=float, default=1.0)
    p.add_argument("--patience",     type=int,   default=15)
    # Logging
    p.add_argument("--run_name",  default=None)
    p.add_argument("--project",   default="torso-transformer")
    p.add_argument("--ckpt_dir",  default="checkpoints")
    return p.parse_args()


def main():
    args = parse_args()

    # ---- Dataset --------------------------------------------------------
    npy_paths = sorted(Path(args.data_dir).glob("*.npy"))
    if not npy_paths:
        raise FileNotFoundError(f"No .npy files found in {args.data_dir}")

    # Shuffle and split at sequence level to avoid data leakage
    random.shuffle(npy_paths)
    n_val = max(1, int(len(npy_paths) * args.val_ratio))
    val_paths   = npy_paths[:n_val]
    train_paths = npy_paths[n_val:]

    train_ds = PoseSequenceDataset(train_paths, window=args.window, stride=args.stride,  ema_alpha=args.ema_alpha)
    val_ds   = PoseSequenceDataset(val_paths,   window=args.window, stride=args.window,  ema_alpha=args.ema_alpha)

    # Fit normaliser on training sequences only
    normalizer = RunningNormalizer().fit(train_ds)

    train_loader = DataLoader(train_ds, batch_size=args.batch_size, shuffle=True,  num_workers=args.num_workers, pin_memory=True)
    val_loader   = DataLoader(val_ds,   batch_size=args.batch_size, shuffle=False, num_workers=args.num_workers, pin_memory=True)

    # ---- Model ----------------------------------------------------------
    lit = TorsoLitModule(
        d_model=args.d_model,
        nhead=args.nhead,
        num_layers=args.num_layers,
        dim_ff=args.dim_ff,
        dropout=args.dropout,
        causal=args.causal,
        lr=args.lr,
        weight_decay=args.weight_decay,
        rp_weight=args.rp_weight,
        yaw_weight=args.yaw_weight,
    )
    lit.set_normalizer(normalizer)

    # ---- Logger & callbacks --------------------------------------------
    wandb_logger = WandbLogger(
        project=args.project,
        name=args.run_name,
        log_model=True,
        config=vars(args),
    )

    ckpt_cb = ModelCheckpoint(
        dirpath=args.ckpt_dir,
        filename="{epoch:03d}-{val/loss:.4f}",
        monitor="val/loss",
        mode="min",
        save_top_k=3,
    )
    early_stop_cb = EarlyStopping(
        monitor="val/loss",
        patience=args.patience,
        mode="min",
    )

    # ---- Trainer -------------------------------------------------------
    trainer = L.Trainer(
        max_epochs=args.max_epochs,
        logger=wandb_logger,
        callbacks=[ckpt_cb, early_stop_cb],
        log_every_n_steps=10,
        gradient_clip_val=1.0,
    )
    trainer.fit(lit, train_loader, val_loader)
    wandb.finish()


if __name__ == "__main__":
    main()
