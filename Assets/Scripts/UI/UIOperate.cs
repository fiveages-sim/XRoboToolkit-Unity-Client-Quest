using System;
using System.Collections.Generic;
using System.Net;
using Robot;
using Robot.Conf;
// using Unity.XR.PICO.TOBSupport;
// using Unity.XR.PXR;
using TMPro;
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

        // Temporary debug: small Probe start/stop beside "Network" title text.
        EnsureControllerProbeButtons();
    }

    /// <summary>
    /// Tiny temp buttons glued to the right of the "Network" glyphs.
    /// Own Canvas+sortOrder so Right-panel overlap cannot steal Start clicks.
    /// Safe to delete later.
    /// </summary>
    private void EnsureControllerProbeButtons()
    {
        // Clean previous wrong placements (title-row / Prefer USB / reconnect).
        if (ReconnectBtn != null)
        {
            DestroyProbeButtonIfPresent(ReconnectBtn.transform.parent, "ProbeStartBtn");
            DestroyProbeButtonIfPresent(ReconnectBtn.transform.parent, "ProbeStopBtn");
            DestroyProbeButtonIfPresent(ReconnectBtn.transform.parent, "ProbeBtnRow");
        }

        if (NetshareTog != null)
        {
            Transform preferRow = NetshareTog.transform.parent;
            DestroyProbeButtonIfPresent(preferRow, "ProbeStartBtn");
            DestroyProbeButtonIfPresent(preferRow, "ProbeStopBtn");
            DestroyProbeButtonIfPresent(preferRow, "ProbeBtnRow");
        }

        RectTransform networkLabel = FindNetworkTitleRect();
        if (networkLabel == null)
        {
            Debug.LogWarning("[UIOperate] Network title TMP not found; probe buttons skipped");
            return;
        }

        var tmp = networkLabel.GetComponent<TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.raycastTarget = false;
            // Shrink hit/layout box to glyph width so Start is not under empty TMP rect.
            float textW = Mathf.Max(tmp.preferredWidth + 4f, 70f);
            networkLabel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textW);
        }

        // Old sibling under Title row — remove so it cannot sit under overlap.
        Transform titleRow = networkLabel.parent;
        if (titleRow != null)
        {
            DestroyProbeButtonIfPresent(titleRow, "ProbeBtnRow");
        }

        // Parent under the Network TMP itself, immediately after the glyphs.
        Transform holder = networkLabel.Find("ProbeBtnRow");
        if (holder == null)
        {
            var holderGo = new GameObject(
                "ProbeBtnRow",
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup));
            holderGo.layer = networkLabel.gameObject.layer;
            holderGo.transform.SetParent(networkLabel, false);
            holder = holderGo.transform;

            var hlg = holderGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.padding = new RectOffset(0, 0, 0, 0);
        }

        // Beat overlapping Right/Camera/DataCollection raycasts (START was blocked).
        var canvas = holder.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = holder.gameObject.AddComponent<Canvas>();
        }

        canvas.overrideSorting = true;
        canvas.sortingOrder = 500;
        if (holder.GetComponent<GraphicRaycaster>() == null)
        {
            holder.gameObject.AddComponent<GraphicRaycaster>();
        }

        var holderRt = holder.GetComponent<RectTransform>();
        holderRt.anchorMin = new Vector2(1f, 0.5f);
        holderRt.anchorMax = new Vector2(1f, 0.5f);
        holderRt.pivot = new Vector2(0f, 0.5f);
        holderRt.sizeDelta = new Vector2(110f, 28f);
        holderRt.anchoredPosition = new Vector2(6f, 0f);
        holder.SetAsLastSibling();

        CreateSmallProbeButton(holder, "ProbeStartBtn", "P>", OnProbeStartBtn);
        CreateSmallProbeButton(holder, "ProbeStopBtn", "Px", OnProbeStopBtn);
    }

    private static RectTransform FindNetworkTitleRect()
    {
        foreach (var tmp in FindObjectsOfType<TextMeshProUGUI>(true))
        {
            if (tmp == null)
            {
                continue;
            }

            string t = tmp.text != null ? tmp.text.Trim() : string.Empty;
            if (t == "Network")
            {
                return tmp.rectTransform;
            }
        }

        return null;
    }

    private static void DestroyProbeButtonIfPresent(Transform parent, string name)
    {
        if (parent == null)
        {
            return;
        }

        Transform t = parent.Find(name);
        if (t != null)
        {
            Destroy(t.gameObject);
        }
    }

    private static void CreateSmallProbeButton(
        Transform parent,
        string name,
        string label,
        UnityEngine.Events.UnityAction onClick)
    {
        if (parent.Find(name) != null)
        {
            return;
        }

        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = 52f;
        le.preferredHeight = 28f;
        le.minWidth = 52f;
        le.flexibleWidth = 0f;

        var img = go.GetComponent<Image>();
        img.color = new Color(0.25f, 0.45f, 0.75f, 0.95f);
        img.raycastTarget = true;

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.interactable = true;
        btn.onClick.AddListener(onClick);

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textGo.transform.SetParent(go.transform, false);
        textGo.layer = go.layer;
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        var text = textGo.GetComponent<Text>();
        text.text = label;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.fontSize = 14;
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.raycastTarget = false;
    }

    private static ControllerSourceProbe GetOrCreateProbe()
    {
        var src = FindObjectOfType<QuestTrackingDataSource>();
        if (src == null)
        {
            Toast.Show("QuestSource missing");
            return null;
        }

        return src.EnsureControllerSourceProbe();
    }

    private void OnProbeStartBtn()
    {
        var probe = GetOrCreateProbe();
        if (probe == null)
        {
            return;
        }

        string tag = "bug_" + DateTime.UtcNow.ToString("HHmmss");
        probe.StartProbe(tag);
        string path = probe.FilePath ?? "(pending)";
        LogWindow.Info($"Controller probe START tag={tag}\n{path}");
        Toast.Show("Probe START");
        Debug.Log($"[UIOperate] ControllerSourceProbe START {path}");
    }

    private void OnProbeStopBtn()
    {
        var probe = GetOrCreateProbe();
        if (probe == null)
        {
            return;
        }

        string path = probe.FilePath;
        probe.StopProbe();
        LogWindow.Info($"Controller probe STOP\n{path}");
        Toast.Show("Probe STOP — adb pull files/");
        Debug.Log($"[UIOperate] ControllerSourceProbe STOP {path}");
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
        // Pico community style: Connect with Prefer USB → 127.0.0.1 (no Enter IP).
        if (PreferUsbEnabled)
        {
            TcpConnect(UsbLoopbackAddress);
            return;
        }

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
                "Prefer USB on: pose " + UsbAdbTeleop.LoopbackAddress + ":" +
                UsbAdbTeleop.PosePort + ", camera cmd :" + UsbAdbTeleop.CameraCommandPort +
                " + video forward :" + UsbAdbTeleop.CameraVideoPort +
                " (auto tunnels via PC Service USB hook). " +
                "Enter IP LAN address is kept while connected.");
            _nextAutoConnectTime = 0f;
        }
        else
        {
            LogWindow.Info(
                "Prefer USB off: use Enter IP with the PC LAN address (e.g. 192.168.110.37).");
        }
    }

    /// <summary>
    /// Prefer USB: only auto-connect loopback when pose is NOT already on a working
    /// explicit LAN IP. Enter IP 192.168.x.x must stick (hybrid); never steal Wi‑Fi.
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

        // Working LAN (or any non-loopback) wins until user hits Connect / toggles USB.
        if (TcpHandler.State == SocketState.WORKING &&
            !IsUsbLoopback(TcpHandler.GetTargetIP))
        {
            return;
        }

        if (TcpHandler.State == SocketState.WORKING && IsUsbLoopback(TcpHandler.GetTargetIP))
        {
            return;
        }

        if (Time.realtimeSinceStartup < _nextAutoConnectTime)
        {
            return;
        }

        _nextAutoConnectTime = Time.realtimeSinceStartup + AutoConnectRetrySeconds;
        Debug.Log("USB auto-connect: " + UsbLoopbackAddress + ":" + TcpHandler.TCP_PORT);
        TcpConnect(UsbLoopbackAddress);
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
        if (on)
        {
            // Ensure PC gets a fresh packet after re-enabling send.
            TcpHandler.ResetTrackingPublishGate();
        }
        else
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