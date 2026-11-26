# XRoboToolkit-Unity-Client-Quest

Meta Quest Unity client for [XRoboToolkit](https://github.com/XR-Robotics) robot teleoperation.

**Source:** https://github.com/XR-Robotics/XRoboToolkit-Unity-Client-Quest

Related: [XRoboToolkit-Unity-Client](https://github.com/XR-Robotics/XRoboToolkit-Unity-Client) (Pico) · [RobotVisionUnityPluginQuest](https://github.com/XR-Robotics/RobotVisionUnityPluginQuest) (Android decode/render)

## Features

- Pose sync (head / controller / hand) → PC `roboticsservice` (`:63901`)
- Prefer USB (`adb reverse`) or Wi‑Fi / Enter IP
- Remote Vision: receive H.264 from fa-py / Orin / PICO 4U (`:13579` cmd + `:12345` video)
- Local pose recording

Unity **2021.3.45f2** · Meta XR Interaction SDK 72 · Oculus XR Plugin 3.4.1

## Architecture (pose vs camera)

| Path | Direction | Ports | Notes |
|------|-----------|-------|--------|
| Tracking | Headset → PC | `63901` | Independent of camera |
| Camera command | Headset → PC (or other sender) | `13579` | Listen dialog IP = command host |
| Camera video | PC → Headset | `12345` | PC reverse-connects to callback IP in `OPEN_CAMERA` |

Pose TCP target (`127.0.0.1` vs `192.168.x.x`) does **not** lock the camera path. Camera IP can be any push sender; when that sender is the same PC, use USB loopback or LAN as below.

**USB (Pico community model):** PC Service hook maintains

- `adb reverse 63901` + `13579` (headset → PC)
- `adb forward 12345` (PC → headset)

Open **XRoboToolkit-PC-Service** after `fa-py` `init` / USB helper install. Manual fallback lives in fa-py: `vr_pose_publisher/scripts/adb_reverse_teleop.sh` (not duplicated in this repo).

## Usage

### 1. PC service

- Windows: `\Program Files (x86)\roboticsservice\runService.bat`
- Linux: `/opt/apps/roboticsservice/runService.sh`  
  (with fa-py USB helper: tunnels refresh while service runs; log `/tmp/xrt-adb-reverse.log`)

### 2. Quest → PC (pose)

**Wi‑Fi:** same LAN → Enter IP `192.168.x.x` → Head/Controller/Hand → **Send**. Prefer USB **off** if you want to stay on LAN.

**USB:** Prefer USB on (or Enter IP / Connect `127.0.0.1`). Status: `PC Service: 127.0.0.1 (USB)`.

Hybrid: an explicit LAN Enter IP **stays** while connected; Prefer USB only auto-fills loopback when disconnected. Tap **Connect** (Prefer USB on) to switch back to USB.

### 3. Remote Vision (optional)

Select **ZEDMINI** (`camera=ZED`) for fa-py `/head_camera/rgb` (not PICO4U / VR).

- **Wi‑Fi:** Listen → PC LAN IP (e.g. `192.168.110.37`)
- **USB:** Listen → `127.0.0.1` (needs reverse `13579` + forward `12345`)

After Listen, PC may log `推流会话启动` before the first frame: encoder init + connect retries to `:12345` (fa-py ~15s budget). Quest only waits for decoder texture readiness (no long fixed sleep). If PC logs “连不上头显视频端口”, close the camera window and Listen again.

Other sources: **PICO4U** → PICO LAN IP; **ZED/Orin** → [OrinVideoSender](https://github.com/XR-Robotics/XRoboToolkit-Orin-Video-Sender) IP (or `127.0.0.1` with USB tunnels).

### 4. Record

Tracking toggles → Record (device `/Download`; see Log).

## Panel

| Area | Notes |
|------|--------|
| Network | SN / IP / FPS / PC Service / Enter IP / Prefer USB |
| Tracking | Head, Controller, Hand, Send |
| Remote Vision | Video source, Listen |
| Data Collection | Record pose |

![ui.png](Docs/ui.png)

## Build notes (Linux / Fedora)

**`No usable version of libssl`:** Unity 2021 Roslyn needs OpenSSL 1.1; Fedora ships 3.x. Quit Hub/Editor started as `UnityHub-*.AppImage.real`, then:

```bash
./scripts/open_unity_fedora.sh
# or: unityhub-fedora / unity-editor-fedora -projectpath "$PWD"
```

**`Win32Exception` / `Process.Kill` in device list:** usually adb conflict noise. Prefer **Build** APK → `adb install -r -g Your.apk`.

## Citation

```
@article{zhao2025xrobotoolkit,
  title={XRoboToolkit: A Cross-Platform Framework for Robot Teleoperation},
  author={Zhigen Zhao and Liuchuan Yu and Ke Jing and Ning Yang},
  journal={arXiv preprint arXiv:2508.00097},
  year={2025}
}
```
