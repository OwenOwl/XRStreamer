# XRStreamer

XRStreamer 用于把支持 OpenXR 的头显和手柄姿态与按键状态，通过 UDP 实时发送到 Python 侧。

> _项目也支持通过双目鱼眼相机 + IMU 稳定器进行实时观察，以及组合 IMU 的数据进行录制等（个人使用）。_

如果您已获取到对应的可执行文件，可以只抓取 receiver 分支：

```
git clone -b receiver --single-branch --depth 1 <repo-url>
```

## 接收端

### 环境要求

- Python 3.9+
- Torch

### 导入示例

```python
from XRStreamer.XRClient import XRClient

client = XRClient()
frame = client.get_frame()
print(frame["frame_id"], frame["link_pos"], frame["button_states"])
client.shutdown()
```

### 输出数据格式

- `frame_id`: 帧号
- `recv_time`: 接收时间戳
- `link_pos`: `torch.Tensor(3, 3)`，顺序：头显、左手柄、右手柄
- `link_quat`: `torch.Tensor(3, 4)`，顺序：头显、左手柄、右手柄，格式 `(w, x, y, z)`
- `button_states`: 手柄按键与摇杆状态字典

## 使用流程

### PC & Quest

需 Windows 电脑。

1. 电脑安装并打开 Meta Quest Link（原 Oculus PC 软件）。
2. 使用 USB 线连接 Meta Quest 与电脑。
3. 在头显中进入 Link 模式（PCVR 模式）。

### 软件

运行编译好的可执行程序，或在 Unity 编辑器里 `Play`。

启动后在面板中：

1. 在 `Target IP` 输入传输目标 IP。
2. 如果启用 `dome`（半球面视角）：
   - 设置相机来源（如 `OBS Virtual Camera`）。
   - 设置 IMU 串口（如 `COM3`）。
3. 点击 `Start`。

默认目标端口为 `5005`。默认监听和默认 Unity 配置可直接配对。

### 接收端

运行接收端代码。
