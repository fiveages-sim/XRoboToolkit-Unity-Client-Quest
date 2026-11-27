using System;
using System.IO;
using System.Text;
using Oculus.Interaction.Input;
using UnityEngine;

/// <summary>
/// On-device multi-source controller probe for Quest stick/pose crosstalk.
/// Writes JSONL under Application.persistentDataPath — pull with adb.
///
/// Enable: set <see cref="enabled"/> in inspector, or call StartProbe/StopProbe.
/// While left |axisX| or |axisY| &gt; stickGate (or alwaysSample), append one row
/// every sampleHz with OVR / Visual / Interaction poses side by side.
/// </summary>
public class ControllerSourceProbe : MonoBehaviour
{
    [SerializeField] private QuestTrackingDataSource source;
    [SerializeField] private bool startEnabled;
    [SerializeField] private float sampleHz = 30f;
    [SerializeField] private float stickGate = 0.2f;
    [Tooltip("If false, only sample while either stick is deflected.")]
    [SerializeField] private bool alwaysSample;

    private StreamWriter _writer;
    private string _path;
    private float _nextSample = -1f;
    private bool _running;

    public string FilePath => _path;
    public bool IsRunning => _running;

    /// <summary>Wire source and optionally auto-start (used when added from code).</summary>
    public void Configure(QuestTrackingDataSource dataSource, bool autoStart = true, string tag = "auto")
    {
        source = dataSource;
        if (autoStart)
        {
            StartProbe(tag);
        }
    }

    private void Awake()
    {
        if (source == null)
        {
            source = FindObjectOfType<QuestTrackingDataSource>();
        }
    }

    private void Start()
    {
        if (startEnabled && !_running)
        {
            StartProbe();
        }
    }

    private void OnDestroy()
    {
        StopProbe();
    }

    private void Update()
    {
        if (!_running || source == null)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (now < _nextSample)
        {
            return;
        }

        _nextSample = now + (sampleHz > 0.1f ? 1f / sampleHz : 0.033f);

        Vector2 la = OVRInput.Get(OVRInput.RawAxis2D.LThumbstick, OVRInput.Controller.LTouch);
        Vector2 ra = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick, OVRInput.Controller.RTouch);
        if (!alwaysSample &&
            Mathf.Abs(la.x) < stickGate && Mathf.Abs(la.y) < stickGate &&
            Mathf.Abs(ra.x) < stickGate && Mathf.Abs(ra.y) < stickGate)
        {
            return;
        }

        AppendSample(la, ra);
    }

    public void StartProbe(string tag = "bug")
    {
        if (_running)
        {
            return;
        }

        try
        {
            string dir = Application.persistentDataPath;
            Directory.CreateDirectory(dir);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            _path = Path.Combine(dir, $"controller_src_{tag}_{stamp}.jsonl");
            _writer = new StreamWriter(_path, append: false, Encoding.UTF8) { AutoFlush = true };
            _running = true;
            _nextSample = 0f;
            Debug.Log(
                $"[ControllerSourceProbe] START path={_path}  " +
                "adb: adb shell run-as <package> cat files/controller_src_*.jsonl  " +
                "or adb pull after copying to /sdcard/");
            _writer.WriteLine(
                "{\"type\":\"meta\",\"path\":\"" + Escape(_path) + "\",\"tag\":\"" + Escape(tag) + "\"}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[ControllerSourceProbe] Start failed: {e}");
            StopProbe();
        }
    }

    public void StopProbe()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        try
        {
            _writer?.Flush();
            _writer?.Dispose();
        }
        catch
        {
            // ignore
        }

        _writer = null;
        Debug.Log($"[ControllerSourceProbe] STOP path={_path}");
    }

    private void AppendSample(Vector2 la, Vector2 ra)
    {
        if (_writer == null || source == null)
        {
            return;
        }

        bool lValid = OVRInput.GetControllerPositionValid(OVRInput.Controller.LTouch);
        bool rValid = OVRInput.GetControllerPositionValid(OVRInput.Controller.RTouch);
        bool lTracked = OVRInput.GetControllerPositionTracked(OVRInput.Controller.LTouch);
        bool rTracked = OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch);

        Vector3 lLocal = OVRInput.GetLocalControllerPosition(OVRInput.Controller.LTouch);
        Vector3 rLocal = OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch);
        Quaternion lLocalQ = OVRInput.GetLocalControllerRotation(OVRInput.Controller.LTouch);
        Quaternion rLocalQ = OVRInput.GetLocalControllerRotation(OVRInput.Controller.RTouch);

        Pose lOvr = source.GetControllerPose(Handedness.Left);
        Pose rOvr = source.GetControllerPose(Handedness.Right);

        Pose lVis = default;
        Pose rVis = default;
        bool hasLVis = TryVisualPose(true, out lVis);
        bool hasRVis = TryVisualPose(false, out rVis);

        Pose lTry = default;
        Pose rTry = default;
        bool hasLTry = TryInteractionPose(true, out lTry);
        bool hasRTry = TryInteractionPose(false, out rTry);

        var sb = new StringBuilder(512);
        sb.Append('{');
        sb.Append("\"t\":").Append(Time.unscaledTime.ToString("R")).Append(',');
        sb.Append("\"lax\":").Append(la.x.ToString("R")).Append(',');
        sb.Append("\"lay\":").Append(la.y.ToString("R")).Append(',');
        sb.Append("\"rax\":").Append(ra.x.ToString("R")).Append(',');
        sb.Append("\"ray\":").Append(ra.y.ToString("R")).Append(',');
        sb.Append("\"lValid\":").Append(lValid ? "true" : "false").Append(',');
        sb.Append("\"rValid\":").Append(rValid ? "true" : "false").Append(',');
        sb.Append("\"lTracked\":").Append(lTracked ? "true" : "false").Append(',');
        sb.Append("\"rTracked\":").Append(rTracked ? "true" : "false").Append(',');
        AppendPose(sb, "lLocal", lLocal, lLocalQ);
        sb.Append(',');
        AppendPose(sb, "rLocal", rLocal, rLocalQ);
        sb.Append(',');
        AppendPose(sb, "lOvrW", lOvr.position, lOvr.rotation);
        sb.Append(',');
        AppendPose(sb, "rOvrW", rOvr.position, rOvr.rotation);
        if (hasLVis)
        {
            sb.Append(',');
            AppendPose(sb, "lVis", lVis.position, lVis.rotation);
        }

        if (hasRVis)
        {
            sb.Append(',');
            AppendPose(sb, "rVis", rVis.position, rVis.rotation);
        }

        if (hasLTry)
        {
            sb.Append(',');
            AppendPose(sb, "lTry", lTry.position, lTry.rotation);
        }

        if (hasRTry)
        {
            sb.Append(',');
            AppendPose(sb, "rTry", rTry.position, rTry.rotation);
        }

        sb.Append('}');
        _writer.WriteLine(sb.ToString());
    }

    private bool TryVisualPose(bool left, out Pose pose)
    {
        pose = Pose.identity;
        var vis = left ? source.leftControllerVisual : source.rightControllerVisual;
        if (vis == null)
        {
            return false;
        }

        pose = new Pose(vis.transform.position, vis.transform.rotation);
        return true;
    }

    private bool TryInteractionPose(bool left, out Pose pose)
    {
        pose = Pose.identity;
        var c = left ? source.leftController : source.rightController;
        if (c == null)
        {
            return false;
        }

        return c.TryGetPose(out pose);
    }

    private static void AppendPose(StringBuilder sb, string key, Vector3 p, Quaternion q)
    {
        sb.Append('"').Append(key).Append("\":[");
        sb.Append(p.x.ToString("R")).Append(',');
        sb.Append(p.y.ToString("R")).Append(',');
        sb.Append(p.z.ToString("R")).Append(',');
        sb.Append(q.x.ToString("R")).Append(',');
        sb.Append(q.y.ToString("R")).Append(',');
        sb.Append(q.z.ToString("R")).Append(',');
        sb.Append(q.w.ToString("R")).Append(']');
    }

    private static string Escape(string s)
    {
        return (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
