using System.Net;
using Robot;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class IpInputDialog : MonoBehaviour
{
    private const string PrefsKey = "IpInputDialog.LastIp";

    public TMP_InputField TmpInput;
    public TextMeshProUGUI Remind;
    public Button ConnectBtn;
    public Button CloseBtn;
    public TcpHandler TcpHandler;

    public UIOperate uiRobot;

    private bool _connecting = false;

    private void Awake()
    {
        ConnectBtn.onClick.AddListener(OnConnectBtn);
        CloseBtn.onClick.AddListener(OnCloseBtn);

        if (TmpInput != null && TmpInput.GetComponent<QuestTmpKeyboard>() == null)
        {
            var keyboard = TmpInput.gameObject.AddComponent<QuestTmpKeyboard>();
            // Numbers-friendly keyboard when supported by the overlay.
        }
    }

    private void OnEnable()
    {
        Remind.text = "";
        ConnectBtn.gameObject.SetActive(true);
        _connecting = false;

        if (TmpInput != null && string.IsNullOrWhiteSpace(TmpInput.text))
        {
            TmpInput.text = PlayerPrefs.GetString(PrefsKey, "");
        }
    }

    private void OnCloseBtn()
    {
        _connecting = false;
        gameObject.SetActive(false);
    }

    public void OnConnectBtn()
    {
        if (TmpInput == null || TcpHandler == null)
        {
            SetRemind(LogType.Error, "IP input is not configured!");
            return;
        }

        string ip = NormalizeIp(TmpInput.text);
        TmpInput.text = ip;

        if (!IPAddress.TryParse(ip, out var parsed) ||
            parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            SetRemind(LogType.Error, "The IP format is incorrect!");
            return;
        }

        // 127.0.0.1 on Quest is the headset itself unless USB adb reverse is set up:
        //   adb reverse tcp:63901 tcp:63901
        // Allowed for wired probe; Wi‑Fi teleop should still use the PC LAN IP.
        bool adbWired = IPAddress.IsLoopback(parsed);
        if (adbWired)
        {
            LogWindow.Warn(
                "Connecting via 127.0.0.1 (ADB reverse). " +
                "Requires: adb reverse tcp:63901 tcp:63901");
        }
        else if (uiRobot != null && uiRobot.PreferUsbEnabled)
        {
            // Pico-style: Enter IP stays available; Prefer USB may reclaim once reverse is up.
            LogWindow.Info(
                "Prefer USB is on: a working adb reverse may replace this Wi‑Fi route.");
        }

        PlayerPrefs.SetString(PrefsKey, ip);
        PlayerPrefs.Save();

        SetRemind(LogType.Log, adbWired ? "Connecting (ADB wired)..." : "Connecting...");
        _connecting = true;
        ConnectBtn.gameObject.SetActive(false);

        // Keep main panel PC Service / Reconnect UI in sync with the UDP discovery path.
        if (uiRobot != null)
        {
            uiRobot.TcpConnect(ip);
        }
        else
        {
            TcpHandler.Connect(ip);
        }
    }

    private void Update()
    {
        if (TcpHandler == null || !_connecting)
        {
            return;
        }

        if (TcpHandler.State == SocketState.WORKING)
        {
            if (uiRobot != null)
            {
                uiRobot.ConnectSuccess();
            }

            SetRemind(LogType.Log, "Connected.");
            _connecting = false;
            gameObject.SetActive(false);
        }
        else if (TcpHandler.State == SocketState.CONNECT_ERROR || TcpHandler.State == SocketState.CLOSE)
        {
            if (!string.IsNullOrEmpty(TcpHandler.ConnectErrorInfo))
            {
                SetRemind(LogType.Error, TcpHandler.ConnectErrorInfo);
            }
            else
            {
                SetRemind(LogType.Error, "Connect fail!");
            }

            ConnectBtn.gameObject.SetActive(true);
            _connecting = false;
        }
    }

    private static string NormalizeIp(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        // Strip whitespace / zero-width / full-width digits from Quest keyboard input.
        var trimmed = raw.Trim()
            .Replace('\u3000', ' ')
            .Replace(" ", string.Empty);

        char[] chars = trimmed.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] >= '０' && chars[i] <= '９')
            {
                chars[i] = (char)('0' + (chars[i] - '０'));
            }
            else if (chars[i] == '．')
            {
                chars[i] = '.';
            }
        }

        return new string(chars);
    }

    public void SetRemind(LogType type, string content)
    {
        if (type == LogType.Error)
        {
            Remind.color = Color.red;
        }
        else
        {
            Remind.color = Color.white;
        }

        Remind.text = content;
    }
}
