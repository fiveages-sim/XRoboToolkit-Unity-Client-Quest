using UnityEngine;

/// <summary>
/// While either thumbstick is deflected, freeze <see cref="Target"/> pose.
/// Stops intermittent Meta ray-grab / UISurround paths from moving the world panel
/// during teleop stick use. Cold-start head snap in <see cref="UISurroundDrag"/> is unchanged.
/// </summary>
public class PanelStickFreeze : MonoBehaviour
{
    public Transform Target;

    [Tooltip("Stick magnitude above this locks the panel.")]
    public float StickDeadzone = 0.12f;

    private Vector3 _lockedPos;
    private Quaternion _lockedRot;
    private bool _hasLock;

    private void LateUpdate()
    {
        if (Target == null)
        {
            return;
        }

        Vector2 left = OVRInput.Get(OVRInput.RawAxis2D.LThumbstick, OVRInput.Controller.LTouch);
        Vector2 right = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick, OVRInput.Controller.RTouch);
        bool stickActive = left.sqrMagnitude >= StickDeadzone * StickDeadzone
                           || right.sqrMagnitude >= StickDeadzone * StickDeadzone;

        if (stickActive)
        {
            if (!_hasLock)
            {
                _lockedPos = Target.position;
                _lockedRot = Target.rotation;
                _hasLock = true;
            }

            Target.SetPositionAndRotation(_lockedPos, _lockedRot);
        }
        else
        {
            _lockedPos = Target.position;
            _lockedRot = Target.rotation;
            _hasLock = true;
        }
    }
}
