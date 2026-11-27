using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using Oculus.Interaction.Input.Visuals;
using UnityEngine;

public class QuestTrackingDataSource : MonoBehaviour
{
    public Hmd headset;
    public HandVisual leftHandVisual;
    public HandVisual rightHandVisual;

    public List<Transform> leftHandJoints;
    public List<Transform> rightHandJoints;

    public Controller leftController;
    public Controller rightController;
    public OVRControllerVisual leftControllerVisual;
    public OVRControllerVisual rightControllerVisual;

    [Tooltip("Auto-attach ControllerSourceProbe (does not start until UI / StartProbe).")]
    public bool enableControllerSourceProbe = true;

    private Transform _trackingSpace;
    private Pose _lastValidLeftPose = Pose.identity;
    private Pose _lastValidRightPose = Pose.identity;
    private bool _hasLastValidLeft;
    private bool _hasLastValidRight;
    private ControllerSourceProbe _controllerSourceProbe;

    private Transform TrackingSpace
    {
        get
        {
            if (_trackingSpace == null)
            {
                var rig = FindObjectOfType<OVRCameraRig>();
                if (rig != null)
                {
                    _trackingSpace = rig.trackingSpace;
                }
            }

            return _trackingSpace;
        }
    }

    public ControllerSourceProbe ControllerSourceProbe => EnsureControllerSourceProbe();

    private void Awake()
    {
        EnsureControllerSourceProbe();
    }

    /// <summary>
    /// Ensures probe component exists on this object. Does <b>not</b> start recording;
    /// use UI buttons or <see cref="ControllerSourceProbe.StartProbe"/>.
    /// </summary>
    public ControllerSourceProbe EnsureControllerSourceProbe()
    {
        if (!enableControllerSourceProbe)
        {
            return _controllerSourceProbe;
        }

        if (_controllerSourceProbe == null)
        {
            _controllerSourceProbe = GetComponent<ControllerSourceProbe>();
        }

        if (_controllerSourceProbe == null)
        {
            _controllerSourceProbe = gameObject.AddComponent<ControllerSourceProbe>();
        }

        _controllerSourceProbe.Configure(this, autoStart: false);
        return _controllerSourceProbe;
    }

    /// <summary>
    /// Determines the currently active input device type based on what's being used
    /// </summary>
    /// <returns>2 for hand tracking, 1 for controller, 0 for headset only</returns>
    public int GetActiveInputDevice()
    {
        // Prefer controllers when connected. Quest can keep HandVisual visible while
        // holding Touch controllers; reporting Input=2 makes the PC prefer Hand poses
        // while stick axes still come from Controller JSON → other-arm yaw symptom.
        if (OVRInput.IsControllerConnected(OVRInput.Controller.LTouch) ||
            OVRInput.IsControllerConnected(OVRInput.Controller.RTouch) ||
            leftController.IsConnected || rightController.IsConnected)
        {
            return 1; // controller
        }

        if (leftHandVisual.IsVisible || rightHandVisual.IsVisible)
        {
            return 2; // handtracking
        }

        return 0; // by default return headset
    }

    /// <summary>
    /// Gets the current pose of the VR headset
    /// </summary>
    /// <returns>Tuple containing validity flag and pose data</returns>
    public (bool, Pose) GetHeadsetPose()
    {
        if (headset.TryGetRootPose(out Pose pose))
        {
            return (true, pose);
        }

        return (false, Pose.identity);
    }

    /// <summary>
    /// World-space controller pose from OVRInput LTouch/RTouch (same source as stick axes).
    /// When PositionValid is false, hold the last valid pose (avoids republishing junk /
    /// stale jumps that ATM treats as live absolute tracking).
    /// Avoid Controller.TryGetPose / OVRControllerVisual for the teleop JSON path.
    /// </summary>
    public Pose GetControllerPose(Handedness handedness)
    {
        var controller = handedness == Handedness.Left
            ? OVRInput.Controller.LTouch
            : OVRInput.Controller.RTouch;

        bool valid = OVRInput.GetControllerPositionValid(controller);
        Pose pose = SampleOvrWorldPose(controller);

        if (handedness == Handedness.Left)
        {
            if (valid)
            {
                _lastValidLeftPose = pose;
                _hasLastValidLeft = true;
                return pose;
            }

            return _hasLastValidLeft ? _lastValidLeftPose : pose;
        }

        if (valid)
        {
            _lastValidRightPose = pose;
            _hasLastValidRight = true;
            return pose;
        }

        return _hasLastValidRight ? _lastValidRightPose : pose;
    }

    private Pose SampleOvrWorldPose(OVRInput.Controller controller)
    {
        Vector3 localPos = OVRInput.GetLocalControllerPosition(controller);
        Quaternion localRot = OVRInput.GetLocalControllerRotation(controller);

        var space = TrackingSpace;
        if (space != null)
        {
            return new Pose(space.TransformPoint(localPos), space.rotation * localRot);
        }

        return new Pose(localPos, localRot);
    }

    /// <summary>
    /// Checks if the specified controller is currently active and tracking
    /// </summary>
    public bool IsControllerActive(Handedness handedness)
    {
        var controller = handedness == Handedness.Left
            ? OVRInput.Controller.LTouch
            : OVRInput.Controller.RTouch;
        return OVRInput.GetControllerPositionValid(controller);
    }

    /// <summary>
    /// Checks if hand tracking is currently active for the specified hand
    /// </summary>
    public bool IsHandTrackingActive(Handedness handedness)
    {
        if (handedness == Handedness.Left)
        {
            return leftHandVisual.IsVisible;
        }

        return rightHandVisual.IsVisible;
    }

    /// <summary>
    /// Gets all joint poses for the specified hand
    /// </summary>
    public void GetJoints(Handedness handedness, Pose[] poses)
    {
        var joints = handedness == Handedness.Left ? leftHandJoints : rightHandJoints;

        for (var i = 0; i < joints.Count; i++)
        {
            poses[i] = joints[i].GetPose();
        }
    }
}
