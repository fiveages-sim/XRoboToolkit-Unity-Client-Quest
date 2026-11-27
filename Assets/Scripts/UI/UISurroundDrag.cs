using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Triangle MoveButton: short press toggles panel fold; long press then drag rotates <see cref="Target"/>.
/// </summary>
public class UISurroundDrag : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public Transform Target;
    public GameObject Panel;
    public GameObject OpenImage;
    public GameObject CloseImage;

    private const float RotationFollow = 48f;
    private const float LongPressSeconds = 0.35f;

    private bool _pressed;
    private bool _dragArmed;
    private float _pressTime;
    private Quaternion _originTargetRot;
    private Quaternion _originControllerRot;
    private Vector3 _draggingAngle;
    private OVRInput.Controller _dragController = OVRInput.Controller.RTouch;

    /// <summary>
    /// One-shot head snap for cold start / first load. Do not remove.
    /// </summary>
    private void Awake()
    {
        if (Camera.main != null && Target != null)
        {
            Target.transform.position = Camera.main.transform.position;
        }

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

    public void OnPointerDown(PointerEventData eventData)
    {
        if (Target == null)
        {
            return;
        }

        _pressed = true;
        _dragArmed = false;
        _pressTime = Time.unscaledTime;
        _originTargetRot = Target.rotation;
        _draggingAngle = AdjustEulerAngles(_originTargetRot.eulerAngles);
        _dragController = ResolvePressedController();
        _originControllerRot = OVRInput.GetLocalControllerRotation(_dragController);
    }

    private void Update()
    {
        if (!_pressed || Target == null)
        {
            return;
        }

        if (!_dragArmed)
        {
            if (Time.unscaledTime - _pressTime < LongPressSeconds)
            {
                return;
            }

            // Arm drag from current controller pose so the panel does not jump.
            _dragArmed = true;
            _originTargetRot = Target.rotation;
            _draggingAngle = AdjustEulerAngles(_originTargetRot.eulerAngles);
            _originControllerRot = OVRInput.GetLocalControllerRotation(_dragController);
        }

        Quaternion controllerNow = OVRInput.GetLocalControllerRotation(_dragController);
        Quaternion difference = Quaternion.Inverse(_originControllerRot) * controllerNow;
        Vector3 angleDifference = difference.eulerAngles;
        Vector3 diff = new Vector3(
            (angleDifference.x > 180f) ? angleDifference.x - 360f : angleDifference.x,
            (angleDifference.y > 180f) ? angleDifference.y - 360f : angleDifference.y,
            0f);

        Vector3 target = AdjustEulerAngles(_originTargetRot.eulerAngles + diff);
        target.z = 0f;

        float t = 1f - Mathf.Exp(-RotationFollow * Time.deltaTime);
        _draggingAngle = Vector3.Lerp(_draggingAngle, target, t);
        _draggingAngle = AdjustEulerAngles(_draggingAngle);
        _draggingAngle.z = 0f;
        Target.rotation = Quaternion.Euler(_draggingAngle);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_dragArmed)
        {
            TogglePanelFold();
        }

        _pressed = false;
        _dragArmed = false;
    }

    private static OVRInput.Controller ResolvePressedController()
    {
        if (OVRInput.Get(OVRInput.RawButton.RIndexTrigger, OVRInput.Controller.RTouch)
            || OVRInput.Get(OVRInput.RawButton.RHandTrigger, OVRInput.Controller.RTouch))
        {
            return OVRInput.Controller.RTouch;
        }

        if (OVRInput.Get(OVRInput.RawButton.LIndexTrigger, OVRInput.Controller.LTouch)
            || OVRInput.Get(OVRInput.RawButton.LHandTrigger, OVRInput.Controller.LTouch))
        {
            return OVRInput.Controller.LTouch;
        }

        return OVRInput.Controller.RTouch;
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
