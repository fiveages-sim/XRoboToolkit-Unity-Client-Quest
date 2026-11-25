using LitJson;
using UnityEngine;
using Oculus.Interaction.Input;

namespace Robot
{
    public class TrackingData
    {
        public static bool HeadOn { get; private set; }
        public static bool ControllerOn { get; private set; }
        public static bool HandTrackingOn { get; private set; }
        public static TrackingType TrackingTypeValue { get; private set; }

        private JsonData _motionTrackingJson = new JsonData();
        private JsonData _bodyTrackingJson = new JsonData();
        private JsonData _controllerDataJson = new JsonData();
        private JsonData _leftControllerJson = new JsonData();
        private JsonData _rightControllerJson = new JsonData();
        private JsonData _headJson = new JsonData();

        private JsonData _stateData = new JsonData();
        private JsonData _handData = new JsonData();
        private JsonData _leftHandData = new JsonData();
        private JsonData _rightHandData = new JsonData();

        // Reused buffers to avoid per-frame GC (Pico path reuses JsonData the same way).
        private readonly Pose[] _jointPoseBuffer = new Pose[26];

        private QuestTrackingDataSource _questTrackingDataSource;

        public QuestTrackingDataSource questTrackingDataSource
        {
            get
            {
                if (_questTrackingDataSource == null)
                {
                    _questTrackingDataSource = GameObject.FindObjectOfType<QuestTrackingDataSource>();
                }

                return _questTrackingDataSource;
            }
        }

        /// <summary>
        /// Sets whether head tracking is enabled
        /// </summary>
        /// <param name="on">True to enable head tracking, false to disable</param>
        public static void SetHeadOn(bool on)
        {
            HeadOn = on;
        }

        /// <summary>
        /// Sets whether controller tracking is enabled
        /// </summary>
        /// <param name="on">True to enable controller tracking, false to disable</param>
        public static void SetControllerOn(bool on)
        {
            ControllerOn = on;
        }

        /// <summary>
        /// Sets whether hand tracking is enabled
        /// </summary>
        /// <param name="on">True to enable hand tracking, false to disable</param>
        public static void SetHandTrackingOn(bool on)
        {
            HandTrackingOn = on;
        }

        /// <summary>
        /// Sets the current tracking type (None, Body, Motion)
        /// </summary>
        /// <param name="trackingType">The tracking type to set</param>
        public static void SetTrackingType(TrackingType trackingType)
        {
            TrackingTypeValue = trackingType;
        }

        public static bool HasTracking
        {
            get
            {
                return (HeadOn || ControllerOn || HandTrackingOn ||
                        TrackingTypeValue > TrackingType.None);
            }
        }

        /// <summary>
        /// Gathers all tracking data and populates the provided JsonData with current sensor information
        /// Includes head, controller, and hand tracking data based on enabled features
        /// </summary>
        /// <param name="totalData">The JsonData object to populate with tracking information</param>
        public void Get(ref JsonData totalData)
        {
            try
            {
                //sensor
                double predictTime = Time.unscaledTimeAsDouble * 1000000; // Convert to microseconds
                totalData["predictTime"] = predictTime; //微秒，对应camera录制中帧插入的时间戳
                totalData["appState"] = _stateData;
                _stateData["focus"] = Application.isFocused;

                if (HeadOn)
                {
                    totalData["Head"] = GetHeadTrackingData();
                }
                else
                {
                    if (totalData.ContainsKey("Head"))
                        totalData.Remove("Head");
                }

                if (ControllerOn)
                {
                    JsonData controller = GetLeftRightControllerJsonData(predictTime);
                    totalData["Controller"] = controller;
                }
                else
                {
                    if (totalData.ContainsKey("Controller"))
                        totalData.Remove("Controller");
                }

                if (HandTrackingOn)
                {
                    GetHandJsonData();
                    totalData["Hand"] = _handData;
                }
                else
                {
                    if (totalData.ContainsKey("Hand"))
                        totalData.Remove("Hand");
                }

                // Remove PICO-specific tracking features for now
                // Body and Motion tracking would need to be implemented with OpenXR equivalents
                if (totalData.ContainsKey("Body"))
                    totalData.Remove("Body");
                if (totalData.ContainsKey("Motion"))
                    totalData.Remove("Motion");

                long nsTime = Utils.GetCurrentTimestamp();
                totalData["timeStampNs"] = nsTime;

                // For OpenXR, we determine active input device based on what's available
                int inputDevice = GetActiveInputDevice(); //1
                totalData["Input"] = inputDevice;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"TrackingData.Get() failed: {e.StackTrace}");
                // Initialize minimal data structure to prevent further crashes
                if (!totalData.ContainsKey("predictTime"))
                    totalData["predictTime"] = Time.unscaledTimeAsDouble * 1000000;
                if (!totalData.ContainsKey("timeStampNs"))
                    totalData["timeStampNs"] = System.DateTime.UtcNow.Ticks;
                if (!totalData.ContainsKey("Input"))
                    totalData["Input"] = 0;
            }
        }
        
        /// <summary>
        /// Converts coordinate system handedness by flipping Z-axis and adjusting rotation
        /// Unity uses left-handed coordinates, but output needs right-handed coordinates,
        /// but the output needs to match PICO's right-handed (the same as OpenXR and Robot) system.
        /// This method can convert left-handed coordinates to right-handed, and vice versa.
        /// </summary>
        /// <param name="position">Position vector to convert</param>
        /// <param name="rotation">Rotation quaternion to convert</param>
        void ConvertHandedness(ref Vector3 position, ref Quaternion rotation)
        {
            position.z *= -1;
            rotation.z *= -1;
            rotation.w *= -1;
        }

        /// <summary>
        /// Gets head tracking data including pose and status information
        /// </summary>
        /// <returns>JsonData containing head pose and tracking status</returns>
        private JsonData GetHeadTrackingData()
        {
            var (valid, pose) = questTrackingDataSource.GetHeadsetPose();
            if (valid)
            {
                var position = pose.position;
                var rotation = pose.rotation;
                ConvertHandedness(ref position, ref rotation);
                _headJson["pose"] = GetPoseStr(position, rotation);
                _headJson["status"] = 3; // Match PICO
            }
            else
            {
                _headJson["status"] = 0;
            }

            return _headJson;
        }

        /// <summary>
        /// Gets the currently active input device type
        /// </summary>
        /// <returns>Integer representing the active input device</returns>
        private int GetActiveInputDevice()
        {
            return questTrackingDataSource.GetActiveInputDevice();
        }

        /// <summary>
        /// Fills pose (visual, low-latency) + buttons/axes (OVRInput raw, explicit L/R).
        /// </summary>
        private void FillControllerJson(Handedness handedness, ref JsonData json)
        {
            GetControllerJsonData(handedness, ref json);

            Pose pose = questTrackingDataSource.GetControllerPose(handedness);
            var position = pose.position;
            var rotation = pose.rotation;
            ConvertHandedness(ref position, ref rotation);
            json["pose"] = GetPoseStr(position, rotation);
        }

        /// <summary>
        /// Gets tracking data for both left and right controllers
        /// </summary>
        private JsonData GetLeftRightControllerJsonData(double predictTime)
        {
            FillControllerJson(Handedness.Left, ref _leftControllerJson);
            _controllerDataJson["left"] = _leftControllerJson;

            FillControllerJson(Handedness.Right, ref _rightControllerJson);
            _controllerDataJson["right"] = _rightControllerJson;

            return _controllerDataJson;
        }

        /// <summary>
        /// Meta Quest thumbstick/buttons via OVRInput raw LTouch/RThumbstick masks.
        /// Avoids Interaction ControllerInput (extra frame latency) and Unity XR InputDevice
        /// (can bind the wrong node when hand tracking is on).
        ///
        /// Same Pico / PROTOCOL basis: axisX = left/right (right+), axisY = forward/back (forward+).
        /// PC wrapper negates Y; Z+Yaw mode then uses axisX→yaw, axisY→Z.
        /// </summary>
        private static void GetControllerJsonData(Handedness handedness, ref JsonData json)
        {
            bool isLeft = handedness == Handedness.Left;
            var controllerMask = isLeft ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            var rawAxis = isLeft ? OVRInput.RawAxis2D.LThumbstick : OVRInput.RawAxis2D.RThumbstick;
            var rawClick = isLeft ? OVRInput.RawButton.LThumbstick : OVRInput.RawButton.RThumbstick;
            var rawGrip = isLeft ? OVRInput.RawAxis1D.LHandTrigger : OVRInput.RawAxis1D.RHandTrigger;
            var rawTrigger = isLeft ? OVRInput.RawAxis1D.LIndexTrigger : OVRInput.RawAxis1D.RIndexTrigger;
            // Touch face buttons: left X/Y, right A/B.
            var rawPrimary = isLeft ? OVRInput.RawButton.X : OVRInput.RawButton.A;
            var rawSecondary = isLeft ? OVRInput.RawButton.Y : OVRInput.RawButton.B;
            var rawMenu = isLeft ? OVRInput.RawButton.Start : OVRInput.RawButton.None;

            Vector2 axis2D = OVRInput.Get(rawAxis, controllerMask);

            json["axisX"] = axis2D.x;
            json["axisY"] = axis2D.y;
            json["axisClick"] = OVRInput.Get(rawClick, controllerMask);
            json["grip"] = OVRInput.Get(rawGrip, controllerMask);
            json["trigger"] = OVRInput.Get(rawTrigger, controllerMask);
            json["primaryButton"] = OVRInput.Get(rawPrimary, controllerMask);
            json["secondaryButton"] = OVRInput.Get(rawSecondary, controllerMask);
            json["menuButton"] = rawMenu != OVRInput.RawButton.None &&
                                 OVRInput.Get(rawMenu, controllerMask);
        }


        /// <summary>
        /// Gathers hand tracking data for both left and right hands
        /// </summary>
        private void GetHandJsonData()
        {
            GetOVRHandTrackingData(Handedness.Left, ref _leftHandData);
            _handData["leftHand"] = _leftHandData;

            GetOVRHandTrackingData(Handedness.Right, ref _rightHandData);
            _handData["rightHand"] = _rightHandData;
        }

        /// <summary>
        /// Gets hand tracking data for a specific hand using OVR hand tracking system
        /// </summary>
        /// <param name="handedness">Which hand to get data for (Left or Right)</param>
        /// <param name="json">JsonData object to populate with hand tracking information</param>
        private void GetOVRHandTrackingData(Handedness handedness, ref JsonData json)
        {
            var isActive = questTrackingDataSource.IsHandTrackingActive(handedness);

            json["isActive"] = isActive ? 1U : 0U;
            json["count"] = 26; // Total number of bones
            json["scale"] = 1.0f;

            JsonData jointLocationsJson;
            if (json.ContainsKey("HandJointLocations"))
            {
                jointLocationsJson = json["HandJointLocations"];
            }
            else
            {
                jointLocationsJson = new JsonData();
                jointLocationsJson.SetJsonType(JsonType.Array);
                json["HandJointLocations"] = jointLocationsJson;
            }

            questTrackingDataSource.GetJoints(handedness, _jointPoseBuffer);
            float status = isActive ? 15.0f : 0.0f;

            for (int i = 0; i < _jointPoseBuffer.Length; i++)
            {
                JsonData jointJson;
                if (i < jointLocationsJson.Count)
                {
                    jointJson = jointLocationsJson[i];
                }
                else
                {
                    jointJson = new JsonData();
                    jointLocationsJson.Add(jointJson);
                }

                var position = _jointPoseBuffer[i].position;
                var rotation = _jointPoseBuffer[i].rotation;
                ConvertHandedness(ref position, ref rotation);

                jointJson["p"] = GetPoseStr(position, rotation);
                jointJson["s"] = status;
                // Comply with PICO 4U output
                jointJson["r"] = 0.0f;
            }
        }

        // /// <summary>
        // /// Maps OVRPlugin hand status to OpenXR-compatible status flags
        // /// </summary>
        // private uint GetOVRTrackingStatus(OVRPlugin.HandStatus handStatus)
        // {
        //     uint status = 0;
        //
        //     // Bit layout (semantic mapping based on HandLocationStatus):
        //     // Bit 0: Position valid           => 0x01
        //     // Bit 1: Position tracked         => 0x02
        //     // Bit 2: Orientation valid        => 0x04
        //     // Bit 3: Orientation tracked      => 0x08
        //
        //     if (handStatus == OVRPlugin.HandStatus.HandTracked)
        //     {
        //         status |= 0x01; // Position valid
        //         status |= 0x02; // Position tracked
        //         status |= 0x04; // Orientation valid
        //         status |= 0x08; // Orientation tracked
        //     }
        //
        //     return status;
        // }

        // /// <summary>
        // /// Provides default radius values for OVRPlugin bone types
        // /// </summary>
        // private float GetDefaultJointRadiusForOVRBone(OVRPlugin.BoneId boneId)
        // {
        //     // Default radii based on typical hand joint sizes (in meters)
        //     switch (boneId)
        //     {
        //         case OVRPlugin.BoneId.Hand_WristRoot:
        //             return 0.025f;
        //
        //         // Thumb joints
        //         case OVRPlugin.BoneId.Hand_Thumb0:
        //         case OVRPlugin.BoneId.Hand_Thumb1:
        //         case OVRPlugin.BoneId.Hand_Thumb2:
        //         case OVRPlugin.BoneId.Hand_Thumb3:
        //             return 0.012f;
        //
        //         // Index finger joints
        //         case OVRPlugin.BoneId.Hand_Index1:
        //         case OVRPlugin.BoneId.Hand_Index2:
        //         case OVRPlugin.BoneId.Hand_Index3:
        //             return 0.008f;
        //
        //         // Middle finger joints
        //         case OVRPlugin.BoneId.Hand_Middle1:
        //         case OVRPlugin.BoneId.Hand_Middle2:
        //         case OVRPlugin.BoneId.Hand_Middle3:
        //             return 0.008f;
        //
        //         // Ring finger joints
        //         case OVRPlugin.BoneId.Hand_Ring1:
        //         case OVRPlugin.BoneId.Hand_Ring2:
        //         case OVRPlugin.BoneId.Hand_Ring3:
        //             return 0.007f;
        //
        //         // Little finger joints
        //         case OVRPlugin.BoneId.Hand_Pinky0:
        //         case OVRPlugin.BoneId.Hand_Pinky1:
        //         case OVRPlugin.BoneId.Hand_Pinky2:
        //         case OVRPlugin.BoneId.Hand_Pinky3:
        //             return 0.006f;
        //
        //         default:
        //             return 0.01f; // Generic default
        //     }
        // }

        // /// <summary>
        // /// Provides default radius values for different joint types based on OpenXR recommendations
        // /// </summary>
        // private float GetDefaultJointRadius(XRHandJointID jointID)
        // {
        //     // Default radii based on typical hand joint sizes (in meters)
        //     switch (jointID)
        //     {
        //         case XRHandJointID.Wrist:
        //             return 0.025f;
        //         case XRHandJointID.Palm:
        //             return 0.03f;
        //
        //         // Thumb joints
        //         case XRHandJointID.ThumbMetacarpal:
        //         case XRHandJointID.ThumbProximal:
        //         case XRHandJointID.ThumbDistal:
        //         case XRHandJointID.ThumbTip:
        //             return 0.012f;
        //
        //         // Index finger joints
        //         case XRHandJointID.IndexMetacarpal:
        //         case XRHandJointID.IndexProximal:
        //         case XRHandJointID.IndexIntermediate:
        //         case XRHandJointID.IndexDistal:
        //         case XRHandJointID.IndexTip:
        //             return 0.008f;
        //
        //         // Middle finger joints
        //         case XRHandJointID.MiddleMetacarpal:
        //         case XRHandJointID.MiddleProximal:
        //         case XRHandJointID.MiddleIntermediate:
        //         case XRHandJointID.MiddleDistal:
        //         case XRHandJointID.MiddleTip:
        //             return 0.008f;
        //
        //         // Ring finger joints
        //         case XRHandJointID.RingMetacarpal:
        //         case XRHandJointID.RingProximal:
        //         case XRHandJointID.RingIntermediate:
        //         case XRHandJointID.RingDistal:
        //         case XRHandJointID.RingTip:
        //             return 0.007f;
        //
        //         // Little finger joints
        //         case XRHandJointID.LittleMetacarpal:
        //         case XRHandJointID.LittleProximal:
        //         case XRHandJointID.LittleIntermediate:
        //         case XRHandJointID.LittleDistal:
        //         case XRHandJointID.LittleTip:
        //             return 0.006f;
        //
        //         default:
        //             return 0.01f; // Generic default
        //     }
        // }


        /// <summary>
        /// Converts position and rotation data into a comma-separated string format
        /// </summary>
        /// <param name="position">3D position vector</param>
        /// <param name="rotation">Rotation quaternion</param>
        /// <returns>String representation of pose in format: x,y,z,qx,qy,qz,qw</returns>
        private static string GetPoseStr(Vector3 position, Quaternion rotation)
        {
            return position.x.ToString("R") + "," + position.y.ToString("R") + "," + position.z.ToString("R") + "," +
                   rotation.x.ToString("R") + "," + rotation.y.ToString("R") + "," + rotation.z.ToString("R") + "," +
                   rotation.w.ToString("R");
        }

        public enum HandMode
        {
            Non = 0,
            Controller = 1,
            Hand = 2
        }

        public enum TrackingType
        {
            None = 0,

            // Body and Motion tracking would require OpenXR extensions
            // Keeping enum for compatibility but functionality removed
            Body = 1,
            Motion = 2
        }
    }
}