using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.UI;

public class UISurroundDrag : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public Transform Target;
    public GameObject Panel;
    public GameObject OpenImage;
    public GameObject CloseImage;

    /// <summary>Higher = snappier panel follow while dragging (was 10, felt sluggish).</summary>
    private const float RotationFollow = 48f;

    /// <summary>Below this peak yaw/pitch delta, treat press as click → toggle fold.</summary>
    private const float ClickAngleSlopDeg = 6f;

    private TrackedDeviceModel _pressedDeviceState;
    private IUIInteractor _pressedInteractor;
    private XRUIInputModule _pressedInputModel;

    private Quaternion _originQua;
    private Vector3 _draggingAngle;
    private float _peakAngleDeg;
    private bool _dragArmed;

    /// <summary>
    /// One-shot head snap for cold start / first load. Do not remove.
    /// </summary>
    private void Awake()
    {
        if (Camera.main != null && Target != null)
        {
            Target.transform.position = Camera.main.transform.position;
        }

        // Probabilistic stick↔panel: freeze Main whenever either stick deflects.
        if (Target != null)
        {
            var freeze = Target.GetComponent<PanelStickFreeze>();
            if (freeze == null)
            {
                freeze = Target.gameObject.AddComponent<PanelStickFreeze>();
            }

            freeze.Target = Target;
        }
    }

    private static bool IsThumbstickActive()
    {
        const float dz = 0.12f;
        Vector2 l = OVRInput.Get(OVRInput.RawAxis2D.LThumbstick, OVRInput.Controller.LTouch);
        Vector2 r = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick, OVRInput.Controller.RTouch);
        return l.sqrMagnitude >= dz * dz || r.sqrMagnitude >= dz * dz;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        var deviceEventData = eventData as TrackedDeviceEventData;
        if (deviceEventData == null || Target == null)
        {
            return;
        }

        // Do not start panel drag while teleop sticks are in use.
        if (IsThumbstickActive())
        {
            return;
        }

        _pressedInputModel = eventData.currentInputModule as XRUIInputModule;
        if (_pressedInputModel == null)
        {
            return;
        }

        _pressedInteractor = deviceEventData.interactor;
        _pressedInputModel.GetTrackedDeviceModel(_pressedInteractor, out _pressedDeviceState);
        _originQua = Target.rotation;
        _draggingAngle = AdjustEulerAngles(_originQua.eulerAngles);
        _peakAngleDeg = 0f;
        _dragArmed = false;
    }

    private void Update()
    {
        if (_pressedInteractor == null || _pressedInputModel == null || Target == null)
        {
            return;
        }

        if (IsThumbstickActive())
        {
            _pressedInteractor = null;
            _pressedInputModel = null;
            _dragArmed = false;
            _peakAngleDeg = 0f;
            return;
        }

        _pressedInputModel.GetTrackedDeviceModel(_pressedInteractor, out var deviceModel);

        Quaternion difference = Quaternion.Inverse(_pressedDeviceState.orientation) * deviceModel.orientation;
        Vector3 angleDifference = difference.eulerAngles;
        Vector3 diff = new Vector3(
            (angleDifference.x > 180f) ? angleDifference.x - 360f : angleDifference.x,
            (angleDifference.y > 180f) ? angleDifference.y - 360f : angleDifference.y,
            0f);

        Vector3 target = AdjustEulerAngles(_originQua.eulerAngles + diff);
        target.z = 0f;

        float intentAngle = Quaternion.Angle(
            _originQua,
            Quaternion.Euler(target));
        if (intentAngle > _peakAngleDeg)
        {
            _peakAngleDeg = intentAngle;
        }

        // Hold still for a click-to-fold; only start rotating after slop.
        if (!_dragArmed)
        {
            if (_peakAngleDeg < ClickAngleSlopDeg)
            {
                return;
            }

            _dragArmed = true;
        }

        float t = 1f - Mathf.Exp(-RotationFollow * Time.deltaTime);
        _draggingAngle = Vector3.Lerp(_draggingAngle, target, t);
        _draggingAngle = AdjustEulerAngles(_draggingAngle);
        _draggingAngle.z = 0f;
        Target.rotation = Quaternion.Euler(_draggingAngle);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (Target != null && !_dragArmed && _peakAngleDeg < ClickAngleSlopDeg)
        {
            TogglePanelFold();
        }

        _pressedInteractor = null;
        _pressedInputModel = null;
        _dragArmed = false;
        _peakAngleDeg = 0f;
    }

    private void TogglePanelFold()
    {
        if (Panel == null)
        {
            return;
        }

        // MoveButton stays a Canvas sibling so it remains clickable while Panel is hidden.
        bool show = !Panel.activeSelf;
        Panel.SetActive(show);
        if (OpenImage != null)
        {
            OpenImage.SetActive(show);
        }

        if (CloseImage != null)
        {
            CloseImage.SetActive(!show);
        }
    }

    public static Vector3 AdjustEulerAngles(Vector3 angles)
    {
        angles.x = AdjustAngle(angles.x);
        angles.y = AdjustAngle(angles.y);
        angles.z = AdjustAngle(angles.z);
        return angles;
    }

    public static float AdjustAngle(float angle)
    {
        if (angle > 180f)
        {
            angle -= 360f;
        }
        else if (angle < -180f)
        {
            angle += 360f;
        }

        return angle;
    }
}
