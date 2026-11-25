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

    /// <summary>
    /// Determines the currently active input device type based on what's being used
    /// </summary>
    /// <returns>2 for hand tracking, 1 for controller, 0 for headset only</returns>
    public int GetActiveInputDevice()
    {
        // handtracking 2
        // controller 1
        // headset 0
        if (leftHandVisual.IsVisible || rightHandVisual.IsVisible)
        {
            return 2; // handtracking
        }

        if (leftController.IsConnected || rightController.IsConnected)
        {
            return 1; // controller
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
    /// World-space controller pose from the visual root (low latency).
    /// Do not use Controller.TryGetPose here — TrackingToWorldTransformer adds noticeable teleop delay.
    /// </summary>
    public Pose GetControllerPose(Handedness handedness)
    {
        if (handedness == Handedness.Left)
        {
            var pos = leftControllerVisual.transform.position;
            var rot = leftControllerVisual.transform.rotation;
            return new Pose(pos, rot);
        }

        var rpos = rightControllerVisual.transform.position;
        var rrot = rightControllerVisual.transform.rotation;
        return new Pose(rpos, rrot);
    }

    /// <summary>
    /// Checks if the specified controller is currently active and tracking
    /// </summary>
    public bool IsControllerActive(Handedness handedness)
    {
        if (handedness == Handedness.Left)
        {
            return leftController.IsPoseValid;
        }

        return rightController.IsPoseValid;
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
