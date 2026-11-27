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

        // Hand is heavier than head/controller; rebuild + attach Hand at most 60 Hz.
        // Head/Controller still follow the display tick. Unchanged sources are omitted from
        // the JSON (Pico B0 style) so PC keeps its cache — otherwise Hand would still look
        // like ~120 Hz on ROS while only the cached blob was being resent with Head.
        private const float HandSampleHz = 60f;
        private const double HandSampleInterval = 1.0 / HandSampleHz;
        // Quantize for publish gate / omit-unchanged (1 mm / ~0.05 quat unit).
        private const float ContentFpPosScale = 1000f;
        private const float ContentFpRotScale = 2000f;
        private const float ContentFpAxisScale = 100f;

        private double _nextHandSampleUnscaled = -1.0;
        private bool _handJsonReady;
        private ulong _cachedHandContentFp;
        private ulong _lastPublishedHeadFp;
        private ulong _lastPublishedControllerFp;
        private ulong _lastPublishedHandFp;
        private int _lastPublishedInput = int.MinValue;
        private bool _lastPublishedFocus = true;
        private bool _forceFullPublish = true;

        // Lightweight on-device counters (log every ~2s) so probe vs source is debuggable.
        private float _gateLogDeadline;
        private int _gateSamples;
        private int _gatePublishes;
        private int _gateSkips;
        private int _gateHandAttached;

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
        /// Clears the unchanged-publish gate so the next Get publishes a full bundle.
        /// Call when SendTrackingData turns on.
        /// </summary>
        public void ResetPublishGate()
        {
            _forceFullPublish = true;
            _handJsonReady = false;
            _nextHandSampleUnscaled = -1.0;
            _lastPublishedHeadFp = 0;
            _lastPublishedControllerFp = 0;
            _lastPublishedHandFp = 0;
            _lastPublishedInput = int.MinValue;
        }

        /// <summary>
        /// Gathers tracking data into <paramref name="totalData"/>.
        /// Returns false when nothing needs publishing (all enabled sources unchanged and
        /// Hand not on a 60 Hz sample tick). Unchanged Head/Controller are omitted from the
        /// JSON; Hand is attached only on its 60 Hz sample ticks.
        /// Pass <paramref name="omitUnchangedSources"/> = false for local recording dumps
        /// that need a full bundle every call.
        /// </summary>
        public bool Get(ref JsonData totalData, bool omitUnchangedSources = true)
        {
            try
            {
                _gateSamples++;
                bool force = _forceFullPublish || !omitUnchangedSources;
                bool focus = Application.isFocused;
                int inputDevice = GetActiveInputDevice();

                ulong headFp = 0;
                bool headEnabled = HeadOn;
                if (headEnabled)
                {
                    headFp = FillHeadTrackingData();
                }

                ulong controllerFp = 0;
                bool controllerEnabled = ControllerOn;
                if (controllerEnabled)
                {
                    controllerFp = FillLeftRightControllerJsonData();
                }

                bool handEnabled = HandTrackingOn;
                bool handSampledThisFrame = false;
                if (handEnabled)
                {
                    // Recording wants every frame; teleop caps Hand attach at 60 Hz.
                    if (!omitUnchangedSources)
                    {
                        _cachedHandContentFp = FillHandJsonData();
                        _handJsonReady = true;
                        handSampledThisFrame = true;
                    }
                    else
                    {
                        handSampledThisFrame = TryRefreshHandJson();
                    }
                }
                else
                {
                    _handJsonReady = false;
                }

                bool includeHead = headEnabled &&
                                   (force || headFp != _lastPublishedHeadFp);
                bool includeController = controllerEnabled &&
                                         (force || controllerFp != _lastPublishedControllerFp);
                // Attach Hand only on sample ticks ⇒ wire Hand rate ≤ 60 Hz even while Head
                // is publishing at display rate.
                bool includeHand = handEnabled &&
                                   (handSampledThisFrame || !omitUnchangedSources);
                bool includeMeta = force ||
                                   focus != _lastPublishedFocus ||
                                   inputDevice != _lastPublishedInput;

                if (omitUnchangedSources &&
                    !includeHead && !includeController && !includeHand && !includeMeta)
                {
                    _gateSkips++;
                    MaybeLogGateStats();
                    return false;
                }

                double predictTime = Time.unscaledTimeAsDouble * 1000000;
                totalData["predictTime"] = predictTime;
                totalData["appState"] = _stateData;
                _stateData["focus"] = focus;
                totalData["timeStampNs"] = Utils.GetCurrentTimestamp();
                totalData["Input"] = inputDevice;

                if (includeHead)
                {
                    totalData["Head"] = _headJson;
                    _lastPublishedHeadFp = headFp;
                }
                else if (totalData.ContainsKey("Head"))
                {
                    totalData.Remove("Head");
                }

                if (includeController)
                {
                    totalData["Controller"] = _controllerDataJson;
                    _lastPublishedControllerFp = controllerFp;
                }
                else if (totalData.ContainsKey("Controller"))
                {
                    totalData.Remove("Controller");
                }

                if (includeHand)
                {
                    totalData["Hand"] = _handData;
                    _lastPublishedHandFp = _cachedHandContentFp;
                    _gateHandAttached++;
                }
                else if (totalData.ContainsKey("Hand"))
                {
                    totalData.Remove("Hand");
                }

                if (totalData.ContainsKey("Body"))
                    totalData.Remove("Body");
                if (totalData.ContainsKey("Motion"))
                    totalData.Remove("Motion");

                _lastPublishedFocus = focus;
                _lastPublishedInput = inputDevice;
                _forceFullPublish = false;
                _gatePublishes++;
                MaybeLogGateStats();
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"TrackingData.Get() failed: {e.StackTrace}");
                if (!totalData.ContainsKey("predictTime"))
                    totalData["predictTime"] = Time.unscaledTimeAsDouble * 1000000;
                if (!totalData.ContainsKey("timeStampNs"))
                    totalData["timeStampNs"] = System.DateTime.UtcNow.Ticks;
                if (!totalData.ContainsKey("Input"))
                    totalData["Input"] = 0;
                _forceFullPublish = true;
                return true;
            }
        }

        /// <returns>True when Hand joints were rebuilt on this call (≤60 Hz).</returns>
        private bool TryRefreshHandJson()
        {
            double now = Time.unscaledTimeAsDouble;
            if (!_handJsonReady || now >= _nextHandSampleUnscaled)
            {
                _cachedHandContentFp = FillHandJsonData();
                _handJsonReady = true;
                _nextHandSampleUnscaled = now + HandSampleInterval;
                return true;
            }

            return false;
        }

        private void MaybeLogGateStats()
        {
            float t = Time.unscaledTime;
            if (_gateLogDeadline <= 0f)
            {
                _gateLogDeadline = t + 2f;
                return;
            }

            if (t < _gateLogDeadline)
            {
                return;
            }

            float dt = Mathf.Max(0.001f, 2f);
            Debug.Log(
                $"[TrackingGate] samples={_gateSamples / dt:F0}/s publish={_gatePublishes / dt:F0}/s " +
                $"skip={_gateSkips / dt:F0}/s handAttach={_gateHandAttached / dt:F0}/s " +
                $"(Hand cap {HandSampleHz:F0}Hz; omit unchanged Head/Controller)");
            _gateSamples = 0;
            _gatePublishes = 0;
            _gateSkips = 0;
            _gateHandAttached = 0;
            _gateLogDeadline = t + 2f;
        }

        private static ulong MixContentFp(ulong fp, long value)
        {
            return MixContentFp(fp, unchecked((ulong)value));
        }

        private static ulong MixContentFp(ulong fp, ulong value)
        {
            unchecked
            {
                fp ^= value;
                fp *= 1099511628211UL; // FNV prime
                return fp;
            }
        }

        private static ulong MixPoseContentFp(ulong fp, Vector3 position, Quaternion rotation)
        {
            fp = MixContentFp(fp, (long)(position.x * ContentFpPosScale));
            fp = MixContentFp(fp, (long)(position.y * ContentFpPosScale));
            fp = MixContentFp(fp, (long)(position.z * ContentFpPosScale));
            fp = MixContentFp(fp, (long)(rotation.x * ContentFpRotScale));
            fp = MixContentFp(fp, (long)(rotation.y * ContentFpRotScale));
            fp = MixContentFp(fp, (long)(rotation.z * ContentFpRotScale));
            fp = MixContentFp(fp, (long)(rotation.w * ContentFpRotScale));
            return fp;
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
        /// Fills head JSON and returns a quantized content fingerprint contribution.
        /// </summary>
        private ulong FillHeadTrackingData()
        {
            ulong fp = 14695981039346656037UL;
            var (valid, pose) = questTrackingDataSource.GetHeadsetPose();
            if (valid)
            {
                var position = pose.position;
                var rotation = pose.rotation;
                ConvertHandedness(ref position, ref rotation);
                _headJson["pose"] = GetPoseStr(position, rotation);
                _headJson["status"] = 3; // Match PICO
                fp = MixPoseContentFp(fp, position, rotation);
                fp = MixContentFp(fp, 3);
            }
            else
            {
                _headJson["status"] = 0;
                fp = MixContentFp(fp, 0);
            }

            return fp;
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
        /// Fills pose + buttons/axes from the same OVRInput LTouch/RTouch source.
        /// </summary>
        private ulong FillControllerJson(Handedness handedness, ref JsonData json)
        {
            ulong fp = FillControllerButtons(handedness, ref json);

            Pose pose = questTrackingDataSource.GetControllerPose(handedness);
            var position = pose.position;
            var rotation = pose.rotation;
            ConvertHandedness(ref position, ref rotation);
            json["pose"] = GetPoseStr(position, rotation);
            fp = MixPoseContentFp(fp, position, rotation);
            return fp;
        }

        /// <summary>
        /// Fills left/right controller JSON; returns combined content fingerprint.
        /// </summary>
        private ulong FillLeftRightControllerJsonData()
        {
            ulong fp = 14695981039346656037UL;
            fp = MixContentFp(fp, FillControllerJson(Handedness.Left, ref _leftControllerJson));
            _controllerDataJson["left"] = _leftControllerJson;

            fp = MixContentFp(fp, FillControllerJson(Handedness.Right, ref _rightControllerJson));
            _controllerDataJson["right"] = _rightControllerJson;

            return fp;
        }

        /// <summary>
        /// Meta Quest thumbstick/buttons via OVRInput raw LTouch/RThumbstick masks.
        /// Avoids Interaction ControllerInput (extra frame latency) and Unity XR InputDevice
        /// (can bind the wrong node when hand tracking is on).
        ///
        /// Same Pico / PROTOCOL basis: axisX = left/right (right+), axisY = forward/back (forward+).
        /// PC wrapper negates Y; Z+Yaw mode then uses axisX→yaw, axisY→Z.
        /// </summary>
        private static ulong FillControllerButtons(Handedness handedness, ref JsonData json)
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
            bool axisClick = OVRInput.Get(rawClick, controllerMask);
            float grip = OVRInput.Get(rawGrip, controllerMask);
            float trigger = OVRInput.Get(rawTrigger, controllerMask);
            bool primary = OVRInput.Get(rawPrimary, controllerMask);
            bool secondary = OVRInput.Get(rawSecondary, controllerMask);
            bool menu = rawMenu != OVRInput.RawButton.None && OVRInput.Get(rawMenu, controllerMask);

            json["axisX"] = axis2D.x;
            json["axisY"] = axis2D.y;
            json["axisClick"] = axisClick;
            json["grip"] = grip;
            json["trigger"] = trigger;
            json["primaryButton"] = primary;
            json["secondaryButton"] = secondary;
            json["menuButton"] = menu;

            ulong fp = 14695981039346656037UL;
            fp = MixContentFp(fp, (long)(axis2D.x * ContentFpAxisScale));
            fp = MixContentFp(fp, (long)(axis2D.y * ContentFpAxisScale));
            fp = MixContentFp(fp, axisClick ? 1L : 0L);
            fp = MixContentFp(fp, (long)(grip * ContentFpAxisScale));
            fp = MixContentFp(fp, (long)(trigger * ContentFpAxisScale));
            fp = MixContentFp(fp, primary ? 1L : 0L);
            fp = MixContentFp(fp, secondary ? 1L : 0L);
            fp = MixContentFp(fp, menu ? 1L : 0L);
            return fp;
        }

        /// <summary>
        /// Rebuilds both hands' joint JSON; returns a quantized fingerprint of joints/active.
        /// </summary>
        private ulong FillHandJsonData()
        {
            ulong fp = 14695981039346656037UL;
            fp = MixContentFp(fp, FillOVRHandTrackingData(Handedness.Left, ref _leftHandData));
            _handData["leftHand"] = _leftHandData;

            fp = MixContentFp(fp, FillOVRHandTrackingData(Handedness.Right, ref _rightHandData));
            _handData["rightHand"] = _rightHandData;
            return fp;
        }

        /// <summary>
        /// Gets hand tracking data for a specific hand using OVR hand tracking system
        /// </summary>
        private ulong FillOVRHandTrackingData(Handedness handedness, ref JsonData json)
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

            ulong fp = 14695981039346656037UL;
            fp = MixContentFp(fp, isActive ? 1L : 0L);

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

                fp = MixPoseContentFp(fp, position, rotation);
            }

            return fp;
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