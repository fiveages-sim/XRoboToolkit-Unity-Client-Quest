using System.Net;
using Robot;
using UnityEngine;

/// <summary>
/// Pico-community-style USB teleop over adb reverse/forward.
/// Pose: reverse 63901. Camera command: reverse 13579. Video: forward 12345.
/// </summary>
public static class UsbAdbTeleop
{
    public const string LoopbackAddress = "127.0.0.1";
    public const int PosePort = 63901;
    public const int CameraCommandPort = 13579;
    public const int CameraVideoPort = 12345;

    public static bool IsLoopback(string ip)
    {
        return !string.IsNullOrEmpty(ip) &&
               IPAddress.TryParse(ip, out var parsed) &&
               IPAddress.IsLoopback(parsed);
    }

    public static bool PreferUsbEnabled
    {
        get
        {
            var operate = Object.FindObjectOfType<UIOperate>();
            return operate != null && operate.PreferUsbEnabled;
        }
    }

    /// <summary>
    /// Camera command host for the dialog default. Only loopback when Prefer USB is on
    /// (do not key off pose-loopback alone — hybrid Wi‑Fi camera must stay possible).
    /// </summary>
    public static string DefaultCameraHostHint =>
        PreferUsbEnabled ? LoopbackAddress : null;

    /// <summary>
    /// Resolve PC host for the camera command channel (headset → PC :13579).
    /// Empty + Prefer USB → 127.0.0.1; explicit LAN IP always wins (hybrid).
    /// </summary>
    public static string ResolvePcHost(string entered)
    {
        string ip = string.IsNullOrEmpty(entered) ? string.Empty : entered.Trim();
        if (string.IsNullOrEmpty(ip))
        {
            return PreferUsbEnabled ? LoopbackAddress : string.Empty;
        }

        return ip;
    }

    /// <summary>
    /// Address embedded in OPEN_CAMERA for PC → headset video.
    /// Loopback ONLY when the camera command host is also loopback (USB end-to-end + adb forward).
    /// Wi‑Fi Listen (e.g. 192.168.110.37) always embeds a real headset LAN IP.
    /// </summary>
    public static string ResolveHeadsetCallbackIp(string cameraCommandHost)
    {
        if (IsLoopback(cameraCommandHost))
        {
            return LoopbackAddress;
        }

        return Utils.GetLocalIPv4(cameraCommandHost);
    }
}
