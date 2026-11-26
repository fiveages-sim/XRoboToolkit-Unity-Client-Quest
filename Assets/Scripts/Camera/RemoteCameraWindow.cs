using System.Collections;
using UnityEngine;
using LitJson;
using Network;
using Robot;
using Robot.V2.Network;
using UnityEngine.UI;


/// <summary>
/// Display window of PC camera
/// Responsible for receiving, decoding, and displaying data
/// </summary>
public class RemoteCameraWindow : MonoBehaviour
{
    public RawImage RemoteCameraImage;
    private Texture2D _texture;
    public Texture Texture => RemoteCameraImage.texture;

    private int _resolutionWidth = 2160;
    private int _resolutionHeight = 2160 / 2 * 4 / 3;
    private int _videoFps = 60;
    private int _bitrate = 40 * 1024 * 1024;

    public RobotVisionUnityPluginQuest questPlugin;

    private void Awake()
    {
        transform.position = Camera.main.transform.position;
        transform.rotation = Camera.main.transform.rotation;
    }

    /// <summary>
    /// Starts listening for remote camera data with specified parameters
    /// Initializes the Quest plugin for video streaming
    /// </summary>
    /// <param name="width">Video width resolution</param>
    /// <param name="height">Video height resolution</param>
    /// <param name="fps">Video frame rate</param>
    /// <param name="bitrate">Video bitrate</param>
    /// <param name="port">Network port for video streaming</param>
    public void StartListen(int width, int height, int fps, int bitrate, int port)
    {
        _resolutionWidth = width;
        _resolutionHeight = height;
        _videoFps = fps;
        _bitrate = bitrate;

        // Update for meta quest
        if (questPlugin != null)
        {
            questPlugin.InitializeQuestPlugin(width, height, port);
        }
        // PICO VR Quest
        // StartCoroutine(OnStartListen(port));
    }

    private void OnDisable()
    {
        // Quest receive path uses RobotVisionUnityPluginQuest, not MediaDecoder.
        // Only release MediaDecoder if that legacy path was actually started.
        MediaDecoder.release();
        if (questPlugin != null)
        {
            questPlugin.CleanupPlugin();
        }

        // StopDecoder tears down :12345 listen — must also drop the control client,
        // otherwise fa-py keeps PINGing on :13579 while video is dead (black RGB).
        try
        {
            if (NetworkCommander.Instance != null)
            {
                NetworkCommander.Instance.CloseCamera();
            }
        }
        catch (System.Exception)
        {
            // ignore if commander not ready during teardown
        }

        if (TcpManager.Instance != null &&
            (Robot.V2.Network.TcpClient.Status == ClientStatus.Connected ||
             Robot.V2.Network.TcpClient.Status == ClientStatus.Connecting))
        {
            TcpManager.Instance.StopClient();
        }

        Debug.Log("RemoteCameraWindow OnDisable");
        TcpHandler.SendFunctionValue("StopReceivePcCamera", "");
    }

    /// <summary>
    /// Handles the close button action - stops camera, cleans up resources and hides window
    /// </summary>
    public void OnCloseBtn()
    {
        // Reset listen button
        // listenBtn.SetOn(false);
        // Hide triggers OnDisable (CLOSE_CAMERA + StopClient + plugin cleanup).
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Coroutine to initialize camera listening on specified port
    /// Sets up texture, media decoder and sends camera parameters to server
    /// </summary>
    /// <param name="port">Port to listen on</param>
    /// <returns>Coroutine enumerator</returns>
    public IEnumerator OnStartListen(int port)
    {
        Debug.Log("StartListen port:" + port);

        _texture = new Texture2D(_resolutionWidth, _resolutionHeight, TextureFormat.RGB24, false, false);
        RemoteCameraImage.texture = _texture;
        yield return null;

        MediaDecoder.initialize((int)_texture.GetNativeTexturePtr(), _resolutionWidth, _resolutionHeight);
        MediaDecoder.startServer(port, false);
        yield return null;

        JsonData cameraParam = new JsonData();
        // Legacy MediaDecoder path (Quest uses questPlugin). Keep peer-aware IP.
        cameraParam["ip"] = UsbAdbTeleop.ResolveHeadsetCallbackIp(null);
        cameraParam["port"] = port;
        cameraParam["width"] = _resolutionWidth;
        cameraParam["height"] = _resolutionHeight;
        cameraParam["fps"] = _videoFps;
        cameraParam["bitrate"] = _bitrate;
        TcpHandler.SendFunctionValue("StartReceivePcCamera", cameraParam.ToJson());
    }

    private void LateUpdate()
    {
        //Keep the window facing the camera at all times
        if (Camera.main != null)
        {
            transform.position = Camera.main.transform.position;
            transform.rotation = Camera.main.transform.rotation;
        }
    }

    private void Update()
    {
        // No need for quest
        // if (_texture != null)
        // {
        //     if (Application.platform == RuntimePlatform.Android)
        //     {
        //         if (MediaDecoder.isUpdateFrame())
        //         {
        //             MediaDecoder.updateTexture();
        //             GL.InvalidateState();
        //         }
        //     }
        // }
    }
}