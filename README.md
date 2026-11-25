# XRoboToolkit-Unity-Client-Quest

Meta Quest Unity client for [XRoboToolkit](https://github.com/XR-Robotics) robot teleoperation.

**Source:** https://github.com/XR-Robotics/XRoboToolkit-Unity-Client-Quest

Related: [XRoboToolkit-Unity-Client](https://github.com/XR-Robotics/XRoboToolkit-Unity-Client) (Pico) · [RobotVisionUnityPluginQuest](https://github.com/XR-Robotics/RobotVisionUnityPluginQuest) (Android decode/render)

## Features

- Pose sync (head / controller / hand) to PC `roboticsservice`
- Prefer USB (`adb reverse`) for low-jitter ~120 Hz teleop; Wi‑Fi / Enter IP still available
- Local pose recording
- Remote stereo vision (PICO 4U / PC / ZED via Orin)

Unity **2021.3.45f2** · Meta XR Interaction SDK 72 · Oculus XR Plugin 3.4.1

## Usage

### 1. Start PC service

- Windows: `\Program Files (x86)\roboticsservice\runService.bat`
- Linux: `/opt/apps/roboticsservice/runService.sh`

Service listens on TCP **63901** (fixed protocol port; unchanged when swapping Meta devices).

### 2. Connect Quest → PC

**Wi‑Fi:** same network → open app → pick discovered IP, or **Enter IP** with the PC LAN address → enable Head/Controller → **Send**.

**USB (recommended):**

```bash
# PC: USB debugging on; one device, or pass serial if several
./scripts/adb_reverse_teleop.sh
# or: adb reverse tcp:63901 tcp:63901
# multi-device: ./scripts/adb_reverse_teleop.sh 63901 <serial>
```

On Quest: check **Prefer USB (adb reverse)** (auto-connects `127.0.0.1`), or Enter IP `127.0.0.1`. Status shows `PC Service: 127.0.0.1 (USB)`.

Re-run reverse after unplug, `adb kill-server`, reboot, or switching headsets. `127.0.0.1` and port `63901` do not change per device.

Prefer USB and Enter IP coexist: LAN Enter IP still works; with Prefer USB on, a working reverse takes priority.

### 3. Record / vision (optional)

- **Record:** select tracking → Record (data under device `/Download`; see Log).
- **PICO 4U → Quest:** both on same LAN → Quest Camera → PICO4U → Listen → PICO IP. B switches SBS / stereo.
- **ZED (Orin):** run [OrinVideoSender](https://github.com/XR-Robotics/XRoboToolkit-Orin-Video-Sender) → Quest Camera → ZEDMINI → Listen → Orin IP.

### Panel (short)

| Area | Notes |
|------|--------|
| Network | SN / IP / FPS / status / PC Service / Enter IP / Prefer USB |
| Tracking | Head, Controller, Hand, Send, Switch w/ A |
| Remote Vision | Video source, Listen |
| Data Collection | Record pose |

![ui.png](Docs/ui.png)

## Citation

```
@article{zhao2025xrobotoolkit,
  title={XRoboToolkit: A Cross-Platform Framework for Robot Teleoperation},
  author={Zhigen Zhao and Liuchuan Yu and Ke Jing and Ning Yang},
  journal={arXiv preprint arXiv:2508.00097},
  year={2025}
}
```
