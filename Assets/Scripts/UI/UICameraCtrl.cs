using System;
using System.Collections;
using System.IO;
using LitJson;
using Network;
using Robot;
using Robot.Conf;
using Robot.Network;
using Robot.V2.Network;
using UnityEngine;
using UnityEngine.UI;
using XRoboToolkit.Network;

public partial class UICameraCtrl : MonoBehaviour
{
    public GameObject RemoteCameraWindowObj;

    public CameraRequestDialog CameraRequestDialog;
    public ResolutionDialog ResolutionDialog;
    public CameraSendInputDialog CameraSendInputDialog;
    public CustomButton ListenCameraBtn;
    public CustomButton CameraSendToBtn;

    public CustomButton ListenPCCameraBtn;
    public Dropdown cameraDropdown;

    public SetLERE setLere;

    private JsonData _recordJson;
    public Text CameraStatusText;

    public Button listenBtn;
    public VideoSourceManager videoSourceManager;

    public TcpManager tcpManager;
    private string logTag => "UICameraCtrl";

    private int streamingPort = 12345;

    [Space(30)] [Header("Record")] public RecordDialog RecordDialog;
    public CustomButton RecordBtn;

    public Toggle trackingToggle;
    public Toggle visionToggle;

    private void Awake()
    {
        RecordBtn.OnChange += OnRecordBtn;
        TcpHandler.ReceiveFunctionEvent += OnNetReceive;

        // Bind event
        tcpManager.OnServerReceived += OnServerReceived;
        tcpManager.OnClientReceived += OnClientReceived;
    }

    private void OnServerReceived(byte[] data)
    {
        // apply protocol
        Utils.WriteLog(logTag, $"OnServerReceived: {data.Length} bytes");

        // Log first few bytes for debugging
        if (data.Length > 0)
        {
            string hexDump = BitConverter.ToString(data, 0, Math.Min(data.Length, 32));
            Utils.WriteLog(logTag, $"First bytes (hex): {hexDump}");
        }

        EventExecutor.ExecuteInUpdate(() =>
        {
            try
            {
                Utils.WriteLog(logTag, $"Processing data...");

                // Check if it's a complete message first
                if (!NetworkDataProtocolSerializer.IsCompleteMessage(data))
                {
                    Utils.WriteLog(logTag, $"Incomplete message received");
                    return;
                }

                var protocol = NetworkDataProtocolSerializer.Deserialize(data);
                Utils.WriteLog(logTag,
                    $"Successfully deserialized: command='{protocol.command}', data length={protocol.data.Length}");

                // Process the command
                if (NetworkCommander.Instance == null)
                {
                    Utils.WriteLog(logTag, $"NetworkCommander.Instance is null");
                    return;
                }

                if (NetworkCommander.Instance.Processor == null)
                {
                    Utils.WriteLog(logTag, $"NetworkCommander.Instance.Processor is null");
                    return;
                }

                bool handled = NetworkCommander.Instance.Processor.ProcessCommand(protocol);
                Utils.WriteLog(logTag, $"Command processed: {handled}");
            }
            catch (Exception e)
            {
                Utils.WriteLog(logTag, $"Error processing command: {e.Message}");
                Utils.WriteLog(logTag, $"Stack trace: {e.StackTrace}");

                // Log detailed buffer analysis
                string bufferDebug = NetworkDataProtocolSerializer.DebugBufferContents(data);
                Utils.WriteLog(logTag, $"Buffer analysis:\n{bufferDebug}");
            }
        });
    }

    private void OnClientReceived(string msg)
    {
        Utils.WriteLog(logTag, $"OnClientReceived: {msg}");
    }

    public void OnListenCameraBtn()
    {
        var on = RemoteCameraWindowObj.activeSelf;
        if (!on)
        {
            // check if the dropdown is updated
            if (cameraDropdown.options == null || cameraDropdown.options.Count == 0) return;

            // get the camera source from the dropdown
            var cameraSource = cameraDropdown.options[cameraDropdown.value].text;

            // Update camera source, including shaders, etc.
            videoSourceManager.UpdateVideoSource(cameraSource);

            // Always show IP dialog (USB: enter 127.0.0.1; Wi‑Fi: PC LAN IP).
            CameraSendInputDialog.Show(RequestCameraStream, null);
        }
        else
        {
            RemoteCameraWindowObj.SetActive(false);
        }
    }

    public void RequestCameraStream(string ip)
    {
        StartCoroutine(RequestCameraStreamCoroutine(ip));
    }

    IEnumerator RequestCameraStreamCoroutine(string ip)
    {
        // Command host: LAN IP (Wi‑Fi) or 127.0.0.1 (USB reverse 13579).
        ip = UsbAdbTeleop.ResolvePcHost(ip);
        if (string.IsNullOrEmpty(ip))
        {
            LogWindow.Error("Camera source IP is empty.");
            yield break;
        }

        if (TcpServer.Status == ServerStatus.Started)
        {
            TcpManager.Instance.StopServer();
        }

        // Always drop a stale control client before Listen (Pico-style).
        if (TcpClient.Status == ClientStatus.Connected ||
            TcpClient.Status == ClientStatus.Connecting)
        {
            TcpManager.Instance.StopClient();
            yield return new WaitForSeconds(0.1f);
        }

        yield return new WaitForSeconds(0.1f);

        var camPara = VideoSourceConfigManager.Instance.CameraParameters;
        var cameraType = VideoSourceConfigManager.Instance.CurrentVideoSource != null
            ? VideoSourceConfigManager.Instance.CurrentVideoSource.camera
            : "";
        if (string.Equals(cameraType, "VR", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cameraType, "PICO4U", StringComparison.OrdinalIgnoreCase))
        {
            LogWindow.Warn(
                $"Video source camera={cameraType} is headset-capture; PC /head_camera/rgb will not push. " +
                "Select ZEDMINI (camera=ZED) for fa-py Remote Vision.");
        }

        streamingPort = UsbAdbTeleop.CameraVideoPort;
        RemoteCameraWindowObj.SetActive(true);
        var remoteWindow = RemoteCameraWindowObj.GetComponent<RemoteCameraWindow>();
        remoteWindow.StartListen(camPara.width, camPara.height, camPara.fps,
            camPara.bitrate, streamingPort);

        // Wait until native texture callback fires. TCP :12345 may still bind a
        // moment later — fa-py _StreamSession retries connect (~15s); no fixed 1.5s sleep.
        float listenDeadline = Time.realtimeSinceStartup + 8f;
        var questPlugin = remoteWindow.questPlugin;
        while (questPlugin != null &&
               !questPlugin.IsPluginInitialized() &&
               Time.realtimeSinceStartup < listenDeadline)
        {
            yield return null;
        }

        if (questPlugin != null && !questPlugin.IsPluginInitialized())
        {
            LogWindow.Error(
                "Quest video decoder did not become ready in time (port " + streamingPort + "). " +
                "Close the camera window and Listen again.");
            yield break;
        }

        yield return null; // one frame for Surface / listen handoff

        setLere.ResetCanvases();

        TcpManager.Instance.StartClient(ip);
        float connectDeadline = Time.realtimeSinceStartup + 5f;
        while (TcpClient.Status != ClientStatus.Connected &&
               TcpClient.Status != ClientStatus.Disconnected &&
               Time.realtimeSinceStartup < connectDeadline)
        {
            yield return null;
        }

        if (TcpClient.Status != ClientStatus.Connected)
        {
            LogWindow.Error(
                $"Camera control connect failed to {ip}:{UsbAdbTeleop.CameraCommandPort}. " +
                (UsbAdbTeleop.IsLoopback(ip)
                    ? "Check adb reverse tcp:13579 and that fa-py video sender is listening."
                    : "Check fa-py --video-port / firewall / same LAN."));
            yield break;
        }

        // Callback IP must match the path: loopback only for USB end-to-end.
        var callbackIp = UsbAdbTeleop.ResolveHeadsetCallbackIp(ip);
        if (callbackIp == "Not found" || string.IsNullOrEmpty(callbackIp))
        {
            LogWindow.Error("Could not resolve headset callback IP for video return path.");
            yield break;
        }

        var customConfig = CameraRequestSerializer.FromCameraParameters(
            camPara,
            0,
            2,
            cameraType,
            callbackIp,
            streamingPort);

        LogWindow.Info(
            $"Remote Vision: command→{ip}:{UsbAdbTeleop.CameraCommandPort}, " +
            $"video←{callbackIp}:{streamingPort}" +
            (UsbAdbTeleop.IsLoopback(ip) ? " (USB: need adb forward tcp:12345)" : " (Wi‑Fi)"));
        LogWindow.Info("Requesting camera stream with config: " + customConfig);

        var data = CameraRequestSerializer.Serialize(customConfig);
        NetworkCommander.Instance.OpenCamera(data);
    }

    private void OnCameraStateChanged(int state)
    {
        CameraStatusText.text = state.ToString();
    }


    private void OnDestroy()
    {
        TcpHandler.ReceiveFunctionEvent -= OnNetReceive;
    }

    private void OnRecordBtn(bool on)
    {
        if (on)
        {
            OpenRecord();
        }
        else
        {
            StopRecord();
        }
    }

    private void OpenRecord()
    {
        if (!visionToggle.isOn && !trackingToggle.isOn)
        {
            LogWindow.Error("Please select at least one option: Tracking or Vision.");
            return;
        }

        // Vision data
        if (visionToggle.isOn)
        {
            if (trackingToggle.isOn)
            {
                // Start together
                RecordDialog.Show(() =>
                {
                    StartRecord(RecordDialog.ResolutionWidth, RecordDialog.ResolutionHeight,
                        RecordDialog.Fps, RecordDialog.Bitrate, true);
                }, null);
            }
            else
            {
                // Vision data only
                RecordDialog.Show(() =>
                {
                    StartRecord(RecordDialog.ResolutionWidth, RecordDialog.ResolutionHeight,
                        RecordDialog.Fps, RecordDialog.Bitrate, false);
                }, null);
            }
        }
        else
        {
            if (trackingToggle.isOn)
            {
                // Only tracking data, use fixed resolution
                OnStartRecordTracking(2160, 810);
                RecordBtn.SetOn(true);
            }
        }
    }

    private void StartRecord(int width, int height, int fps, int bitrate,
        bool onTrackingData)
    {
        LogWindow.Info($"Start record {width}x{height}@{fps} fps {bitrate} bps Tracking: {onTrackingData}");

        Toast.Show("Start Record");
        Debug.Log("StartRecord:" + width + "," + height + "," + fps + "," + bitrate + "," + onTrackingData);
        // CameraHandle.StartCameraPreview(width, height, fps, bitrate, 0,
        //     (int)PXRCaptureRenderMode.PXRCapture_RenderMode_3D,
        //     () =>
        //     {
        //         string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        //         string trackingFileName = $"CameraRecord_{timeStamp}.mp4";
        //         string filePath = Path.Combine("/sdcard/Download/", trackingFileName);
        //         LogWindow.Warn("Vision file path: " + filePath);
        //         CameraHandle.StartRecord(filePath);
        //         _recordTrackingData = onTrackingData;
        //         if (_recordTrackingData)
        //             OnStartRecordTracking(width, height);
        //     });

        if (_recordTrackingData)
        {
            OnStartRecordTracking(width, height);
        }

        RecordBtn.SetOn(true);
        // PreviewCameraTog.gameObject.SetActive(true);
    }

    public void UpdateCameraParamsNew()
    {
        ResolutionDialog.Show("Setting the resolution", (width, height) =>
        {
            Toast.Show("Parameter saved successfully!");
        }, null);
    }

    private void WriteLocalText(string fileName, string content)
    {
        string parentPath = Application.persistentDataPath;
        string filePath = Path.Combine(parentPath, fileName + ".txt");
        using (StreamWriter writer = new StreamWriter(filePath, false))
        {
            writer.Write(content + "\n");
        }

        Debug.Log("The file has been successfully written: " + filePath);
    }

    private void StopRecord()
    {
        Debug.Log(this + "StopRecord");
        LogWindow.Info("Stop record");
        if (_writer != null)
        {
            _writer.Close();
            _writer = null;
        }

        RecordBtn.SetOn(false);
    }

    private void StopSendImage()
    {
        CameraSendToBtn.SetOn(false);
    }

    public void OnRemoteCameraBtn()
    {
        Toast.Show("Request  camera screen on PC!");
        //Request camera screen on PC
        TcpHandler.SendFunctionValue("requestCameraList", "");
    }

    private StreamWriter _writer;
    private bool _recordTrackingData = false;
    private JsonData _trackingJsonData = new JsonData();
    private TrackingData _trackingData = new TrackingData();

    private void OnStartRecordTracking(int width, int height)
    {
        LogWindow.Info($"Start record tracking data: {width}x{height}");
        Debug.Log("OnStartRecordTracking");
        string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string trackingFileName = $"trackingData_{timeStamp}.txt";
        
        string filePath = Path.Combine("/sdcard/Download/", trackingFileName);
        
        #if UNITY_EDITOR
            filePath = Path.Combine(Application.persistentDataPath, trackingFileName);
        #endif
        
        Debug.Log("trackingFilePath:" + filePath);
        LogWindow.Warn("Tracking file path:" + filePath);
        _writer = new StreamWriter(filePath, true);
        _writer.AutoFlush = true; // Enable automatic refresh to prevent data loss

        JsonData cameraParam = new JsonData();
        long nsTime = Utils.GetCurrentTimestamp();
        cameraParam["notice"] =
            "This is the timestamp and head pose information when obtaining the image for the first frame.";
        cameraParam["timeStampNs"] = nsTime;
        //Convert coordinate system to right-handed system (X right, Y up, Z in)

        _writer.WriteLine(cameraParam.ToJson());
        // Update status
        _recordTrackingData = true;
    }

    private void Update()
    {
        if (_recordTrackingData)
        {
            if (_writer != null)
            {
                // Full bundle every frame for the recording dump (no omit/60Hz gate).
                _trackingData.Get(ref _trackingJsonData, omitUnchangedSources: false);
                print(_trackingJsonData.ToString());
                _writer.WriteLine(_trackingJsonData.ToJson());
            }
        }
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        // do nothing for quest
    }
}