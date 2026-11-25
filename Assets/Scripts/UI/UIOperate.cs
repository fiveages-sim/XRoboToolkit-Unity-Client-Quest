using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Robot;
using Robot.Conf;
// using Unity.XR.PICO.TOBSupport;
// using Unity.XR.PXR;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class UIOperate : MonoBehaviour
{
    /// <summary>
    /// Quest wired path via PC-side <c>adb reverse tcp:63901 tcp:63901</c>.
    /// Loopback never changes; only the reverse mapping can drop after unplug/reboot.
    /// </summary>
    private const string UsbLoopbackAddress = "127.0.0.1";
    private const string PreferUsbPrefsKey = "UIOperate.PreferUsb";
    private const float AutoConnectRetrySeconds = 2.0f;
    private const int UsbProbeTimeoutMs = 400;

    public Text SN;
    public Text LocalIP;
    public Text TargetIP;
    public Text TrackNum;
    public Toggle HeadTog;
    public Toggle ControllerTog;
    public Toggle HandTrackingTog;
    public Toggle SendTog;
    public Toggle AcontrolerTog;
    public Dropdown bodyModeDrop;
    public TcpHandler TcpHandler;
    public Text BodyInfo;
    public Toggle HighAccuracy;
    public Text Version;
    public Button ReconnectBtn;
    public Toggle NetshareTog;

    public GameObject Simulator;
    public GameObject CameraObj;
    public GameObject IpInputDialog;
    public GameObject ExtDevPanel;
    public InputActionProperty SendDataAction;

    [Space(30)][Header("Refactoring")] public VideoSourceManager videoSource;
    public VideoSourceConfigManager sourceConfig => videoSource.videoSourceConfigManager;

    public Dropdown videoSourceDropdown;

    private float _nextAutoConnectTime;
    private volatile bool _usbProbeInFlight;
    private volatile bool _usbProbeCompleted;
    private volatile bool _usbProbeSucceeded;

    /// <summary>True when Prefer USB (adb reverse) is checked.</summary>
    public bool PreferUsbEnabled => NetshareTog != null && NetshareTog.isOn;

    // Start is called before the first frame update
    private void Awake()
    {
#if UNITY_EDITOR
        if (Simulator != null)
        {
            Simulator.SetActive(true);
        }
#endif
        // ReconnectBtn.gameObject.SetActive(false);

        // bodyModeDrop.onValueChanged.AddListener(OnBodyModeDrop);
        HeadTog.onValueChanged.AddListener(OnHeadTog);
        ControllerTog.onValueChanged.AddListener(OnControllerTog);
        HandTrackingTog.onValueChanged.AddListener(OnHandTrackingTog);

        SendTog.onValueChanged.AddListener(OnSendTog);
        Version.text = "v: " + Application.version;
        // HighAccuracy.gameObject.SetActive(bodyModeDrop.value > 0);
        // HighAccuracy.onValueChanged.AddListener(OnHighAccuracy);
        ReconnectBtn.onClick.AddListener(OnReconnectBtn);

        // Prefer USB (adb reverse): always available on Quest (unlike Pico enterprise tether).
        if (NetshareTog != null)
        {
            NetshareTog.gameObject.SetActive(true);
            bool preferUsb = PlayerPrefs.GetInt(PreferUsbPrefsKey, 0) == 1;
            NetshareTog.SetIsOnWithoutNotify(preferUsb);
            NetshareTog.onValueChanged.AddListener(OnNetShareTog);
            if (preferUsb)
            {
                _nextAutoConnectTime = 0f;
            }
        }

        // Bypass getting sn via enterprise service to enable data transport
        SetDeviceSN("TestDevice");
        // bool intEnterprise = PXR_Enterprise.InitEnterpriseService();
        // Debug.Log("---InitEnterpriseService :" + intEnterprise);
        // PXR_Enterprise.BindEnterpriseService(OnBindEnterpriseService);

        // if (CameraObj != null)
        // {
        //     CameraObj.SetActive(false);
        // }

        AndroidProxy.CallBack += OnAndroidCallBack;
#if UNITY_EDITOR
        SetDeviceSN("TestDevice");
#endif
        // Refactoring
        sourceConfig.OnInitialized += OnSourceConfigOnOnInitialized;
        // Initialize video source configuration
        sourceConfig.Initialize();

        // Enable SendDataAction for Quest controllers
        if (SendDataAction != null)
        {
            SendDataAction.action.Enable();
            Debug.Log("SendDataAction enabled successfully");
        }
        else
        {
            Debug.LogWarning("SendDataAction.action is null - input action not properly configured");
        }

        // Pose sampling follows the headset display tick (TcpHandler samples in Update).
        // Quest 3 / 3S support 120 Hz; requesting it raises the teleop ceiling from ~90.
        ApplyDisplayFrequency(120f);
    }

    /// <summary>
    /// Requests a headset display refresh rate. Teleop send rate is capped by this value
    /// because tracking is published from Update. Falls back to the highest available
    /// rate if the preferred value is unsupported.
    /// </summary>
    private static void ApplyDisplayFrequency(float preferredHz)
    {
        float[] available = null;
        try
        {
            available = OVRPlugin.systemDisplayFrequenciesAvailable;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"OVRPlugin.systemDisplayFrequenciesAvailable failed: {e.Message}");
        }

        float selected = preferredHz;
        if (available != null && available.Length > 0)
        {
            selected = available[0];
            bool foundPreferred = false;
            for (int i = 0; i < available.Length; i++)
            {
                float hz = available[i];
                if (Mathf.Abs(hz - preferredHz) < 0.1f)
                {
                    selected = hz;
                    foundPreferred = true;
                    break;
                }

                if (hz > selected)
                {
                    selected = hz;
                }
            }

            Debug.Log(
                $"Display frequencies available=[{string.Join(", ", available)}], " +
                $"preferred={preferredHz}, selected={selected} (foundPreferred={foundPreferred})");
        }
        else
        {
            Debug.LogWarning(
                $"No OVR display frequencies reported; requesting {preferredHz} Hz directly.");
        }

        OVRPlugin.systemDisplayFrequency = selected;
        // Keep Unity's player loop aligned with the headset refresh when possible.
        Application.targetFrameRate = Mathf.RoundToInt(selected);
        QualitySettings.vSyncCount = 0;

        float actual = OVRPlugin.systemDisplayFrequency;
        Debug.Log(
            $"Applied display frequency request={selected} Hz, actual={actual} Hz, " +
            $"targetFrameRate={Application.targetFrameRate}");
        LogWindow.Info($"Display refresh: {actual:F0} Hz (teleop sample ceiling)");
    }

    private void OnSourceConfigOnOnInitialized()
    {
        // Update videoSourceDropdown options
        print("OnSourceConfigOnOnInitialized");
        videoSourceDropdown.ClearOptions();
        videoSourceDropdown.AddOptions(sourceConfig.GetVideoSourceNames());
    }

    private void OnAndroidCallBack(string key, string value)
    {
        if (key == "RequestPermissionsBack")
        {
            if (value == "0")
            {
                if (CameraObj != null)
                {
                    CameraObj.SetActive(true);
                }
            }
            else
            {
                Toast.Show("Permission denied!");
            }
        }
    }

    private void OnReconnectBtn()
    {
        TcpHandler.Reconnect();
    }

    public void TcpConnect(string ip)
    {
        TargetIP.text = FormatTargetIpLabel(ip);
        ReconnectBtn.gameObject.SetActive(true);
        TcpHandler.Connect(ip);
        ConnectSuccess();
    }

    public void ConnectSuccess()
    {
        TargetIP.text = FormatTargetIpLabel(TcpHandler.GetTargetIP);
    }

    private static string FormatTargetIpLabel(string ip)
    {
        if (IsUsbLoopback(ip))
        {
            return "PC Service: " + UsbLoopbackAddress + " (USB)";
        }

        return "PC Service: " + ip;
    }

    private static bool IsUsbLoopback(string ip)
    {
        return !string.IsNullOrEmpty(ip) &&
               IPAddress.TryParse(ip, out var parsed) &&
               IPAddress.IsLoopback(parsed);
    }

    private void OnBindEnterpriseService(bool bind)
    {
        Debug.Log("OnBindEnterpriseService " + bind);
        // Quest: Prefer USB toggle is always shown in Awake (adb reverse path).
    }

    private void SetDeviceSN(string sn)
    {
        TcpHandler.SetDeviceSn(sn);
        Debug.Log("SN: " + sn);
        SN.text = "SN: " + sn;
    }

    private void OnNetShareTog(bool ison)
    {
        Debug.Log("OnNetShareTog (Prefer USB):" + ison);
        PlayerPrefs.SetInt(PreferUsbPrefsKey, ison ? 1 : 0);
        PlayerPrefs.Save();

        if (ison)
        {
            LogWindow.Info(
                "Prefer USB on: auto-detecting adb reverse at 127.0.0.1:" +
                TcpHandler.TCP_PORT + ". Run scripts/adb_reverse_teleop.sh on PC.");
            _nextAutoConnectTime = 0f;
            _usbProbeCompleted = false;
            _usbProbeSucceeded = false;
        }
        else
        {
            LogWindow.Info("Prefer USB off: Enter IP / Wi‑Fi route left unchanged.");
        }
    }

    /// <summary>
    /// Pico-style USB priority: when Prefer USB is on, keep trying loopback.
    /// Soft-probes before replacing a working Wi‑Fi route so a missing reverse
    /// does not tear down Enter IP.
    /// </summary>
    private void EnsureUsbTcpConnected()
    {
        if (!PreferUsbEnabled || TcpHandler == null)
        {
            return;
        }

        if (TcpHandler.State == SocketState.CONNECTING)
        {
            return;
        }

        if (TcpHandler.State == SocketState.WORKING && IsUsbLoopback(TcpHandler.GetTargetIP))
        {
            return;
        }

        if (_usbProbeCompleted)
        {
            _usbProbeCompleted = false;
            if (_usbProbeSucceeded)
            {
                if (TcpHandler.State == SocketState.WORKING &&
                    !IsUsbLoopback(TcpHandler.GetTargetIP))
                {
                    Debug.LogWarning(
                        "Replacing non-USB TCP route: target=" + TcpHandler.GetTargetIP);
                }

                Debug.Log(
                    "USB auto-connect: " + UsbLoopbackAddress + ":" + TcpHandler.TCP_PORT);
                TcpConnect(UsbLoopbackAddress);
            }

            return;
        }

        if (_usbProbeInFlight)
        {
            return;
        }

        if (Time.realtimeSinceStartup < _nextAutoConnectTime)
        {
            return;
        }

        _nextAutoConnectTime = Time.realtimeSinceStartup + AutoConnectRetrySeconds;

        if (TcpHandler.State == SocketState.WORKING)
        {
            // Do not call TcpHandler.Connect yet — that would drop Wi‑Fi on probe failure.
            StartUsbLoopbackProbe();
            return;
        }

        Debug.Log("USB auto-connect: " + UsbLoopbackAddress + ":" + TcpHandler.TCP_PORT);
        TcpConnect(UsbLoopbackAddress);
    }

    private void StartUsbLoopbackProbe()
    {
        if (_usbProbeInFlight)
        {
            return;
        }

        _usbProbeInFlight = true;
        _usbProbeCompleted = false;
        _usbProbeSucceeded = false;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            bool ok = ProbeUsbLoopbackReachable();
            _usbProbeSucceeded = ok;
            _usbProbeInFlight = false;
            _usbProbeCompleted = true;
        });
    }

    private static bool ProbeUsbLoopbackReachable()
    {
        Socket socket = null;
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IAsyncResult result = socket.BeginConnect(IPAddress.Loopback, TcpHandler.TCP_PORT, null, null);
            bool signaled = result.AsyncWaitHandle.WaitOne(UsbProbeTimeoutMs);
            if (!signaled || !socket.Connected)
            {
                return false;
            }

            socket.EndConnect(result);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (socket != null)
            {
                try
                {
                    socket.Close();
                }
                catch (Exception)
                {
                    // ignore
                }
            }
        }
    }

    public void OnQuit()
    {
        Application.Quit();
    }

    public void OnExtraDevBtn()
    {
        ExtDevPanel.SetActive(true);
    }

    public void OnWriteIpBtn()
    {
        IpInputDialog.SetActive(true);
    }

    private void OnBodyModeDrop(int index)
    {
        // TODO
        
        // TrackingData.TrackingType tType = (TrackingData.TrackingType)bodyModeDrop.value;
        // int res = 0;
        // bool support = false;

        // TODO: body tracking in Quest
        // MotionTrackerMode trackingMode = PXR_MotionTracking.GetMotionTrackerMode();
        // if (tType == TrackingData.TrackingType.Body)
        // {
        //     if (trackingMode != MotionTrackerMode.BodyTracking)
        //     {
        //         res = PXR_MotionTracking.CheckMotionTrackerModeAndNumber(MotionTrackerMode.BodyTracking,
        //             MotionTrackerNum.TWO);
        //     }
        //
        //     PXR_MotionTracking.GetBodyTrackingSupported(ref support);
        // }
        // else if (tType == TrackingData.TrackingType.Motion)
        // {
        //     if (trackingMode != MotionTrackerMode.MotionTracking)
        //     {
        //         res = PXR_MotionTracking.CheckMotionTrackerModeAndNumber(MotionTrackerMode.MotionTracking,
        //             MotionTrackerNum.ONE);
        //     }
        //
        //     support = true;
        // }

        // if (!support || res != 0)
        // {
        //     BodyInfo.text = "Tracker exception, please connect to calibrate tracker!";
        //     BodyInfo.color = Color.red;
        //
        //     bodyModeDrop.SetValueWithoutNotify(0);
        //     return;
        // }
        //
        // BodyInfo.color = Color.white;
        // BodyInfo.text = "Tracker detection is normal!";
        //
        // UpdateBodyTracking();
    }


    public void OnOpenCameraOperate()
    {
        if (CameraObj != null)
        {
            if (Permission.HasUserAuthorizedPermission(Permission.Camera) &&
                Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                CameraObj.SetActive(!CameraObj.activeSelf);
            }
            else if (!CameraObj.activeSelf)
            {
                var permissionCallbacks = new PermissionCallbacks();
                permissionCallbacks.PermissionGranted += PermissionGranted;
                permissionCallbacks.PermissionDenied += PermissionDenied;

                string[] permissions = { Permission.Camera, Permission.Microphone };
                Permission.RequestUserPermissions(permissions, permissionCallbacks);
            }

            if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead))
            {
                Permission.RequestUserPermission(Permission.ExternalStorageRead);
            }

            if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageWrite))
            {
                Permission.RequestUserPermission(Permission.ExternalStorageWrite);
            }
        }
    }

    private void PermissionDenied(string obj)
    {
        Toast.Show("Permission denied!");
    }

    private void PermissionGranted(string obj)
    {
        if (CameraObj != null)
        {
            CameraObj.SetActive(true);
        }
    }

    private void RefreshLocalIP()
    {
        string localIP = Utils.GetLocalIPv4();
        LocalIP.text = localIP;
    }

    // Obtain the local IPv6 address
    private string GetLocalIPv6()
    {
        string localIP = "Not found";
        foreach (IPAddress ip in Dns.GetHostAddresses(Dns.GetHostName()))
        {
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                localIP = ip.ToString();
                break;
            }
        }

        return localIP;
    }


    private void OnHeadTog(bool on)
    {
        TrackingData.SetHeadOn(on);
    }

    private void OnControllerTog(bool on)
    {
        TrackingData.SetControllerOn(on);

        // Disable on quest for now
        // // Ensure mutual exclusivity with HandTrackingTog
        // if (on && HandTrackingTog.isOn)
        // {
        //     HandTrackingTog.SetIsOnWithoutNotify(false);
        //     TrackingData.SetHandTrackingOn(false);
        // }
    }

    private void OnHandTrackingTog(bool on)
    {
        TrackingData.SetHandTrackingOn(on);

        // Disable on quest for now
        // // Ensure mutual exclusivity with ControllerTog
        // if (on && ControllerTog.isOn)
        // {
        //     ControllerTog.SetIsOnWithoutNotify(false);
        //     TrackingData.SetControllerOn(false);
        // }
    }

    private void OnSendTog(bool on)
    {
        TcpHandler.SendTrackingData = on;
        // Reset FPS
        if (!on)
        {
            FPSDisplay.Reset();
        }
    }

    private void OnHighAccuracy(bool on)
    {
        UpdateBodyTracking();
    }

    private void UpdateBodyTracking()
    {
        TrackingData.TrackingType tType = (TrackingData.TrackingType)bodyModeDrop.value;
        HighAccuracy.gameObject.SetActive(bodyModeDrop.value > 0);
        Debug.Log("UpdateBodyTracking " + tType);
        TrackNum.text = "";
        // TODO: Update for Quest
        // // Set bone length
        // BodyTrackingBoneLength boneLength = new BodyTrackingBoneLength();
        // if (bodyModeDrop.value <= 0)
        // {
        //     int ret = PXR_MotionTracking.StopBodyTracking();
        //     BodyInfo.text = "BodyTracking close";
        // }
        // else
        // {
        //     MotionTrackerConnectState state = new MotionTrackerConnectState();
        //     PXR_MotionTracking.GetMotionTrackerConnectStateWithSN(ref state);
        //     //  PXR_MotionTracking.GetMotionTrackerConnectStateWithSN(ref state);
        //     TrackNum.text = "TrackerNum:" + state.trackerSum;
        //
        //     if (tType == TrackingData.TrackingType.Body)
        //     {
        //         BodyTrackingMode mode = BodyTrackingMode.BTM_FULL_BODY_LOW;
        //         if (HighAccuracy.isOn)
        //         {
        //             mode = BodyTrackingMode.BTM_FULL_BODY_HIGH;
        //         }
        //
        //         // Enable full body motion capture default mode
        //         int ret = PXR_MotionTracking.StartBodyTracking(mode, boneLength);
        //         BodyInfo.text = "Start BodyTracking " + ret;
        //         Debug.Log(" UpdateBodyTracking :" + ret + " trackerSum:" + state.trackerSum);
        //     }
        //     else if (tType == TrackingData.TrackingType.Motion)
        //     {
        //         BodyInfo.text = "Start MotionTracking";
        //     }
        // }

        TrackingData.SetTrackingType(tType);
    }

    private float _lastTime = 0;

    // Update is called once per frame
    void Update()
    {
        if (TcpHandler.State != SocketState.WORKING)
        {
            if (Time.time - _lastTime > 2)
            {
                _lastTime = Time.time;
                RefreshLocalIP();
            }
        }

        EnsureUsbTcpConnected();

        if (AcontrolerTog != null && AcontrolerTog.isOn)
        {
            // Use Input Actions only
            if (SendDataAction != null && SendDataAction.action.WasReleasedThisFrame())
            {
                SendTog.isOn = !SendTog.isOn;
                LogWindow.Info("Sending data: " + SendTog.isOn);
                Debug.Log("SendDataAction triggered - Sending data: " + SendTog.isOn);
            }
        }
    }

    public void OnQuitBtn()
    {
        Application.Quit();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        // Quest may reset refresh rate after focus changes; re-assert 120 when focused.
        if (hasFocus)
        {
            ApplyDisplayFrequency(120f);
        }
    }

    private void OnDestroy()
    {
        // Disable SendDataAction when object is destroyed
        if (SendDataAction != null)
        {
            SendDataAction.action.Disable();
        }
    }
}