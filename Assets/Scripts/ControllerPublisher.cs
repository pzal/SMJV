using System.Collections.Generic;
using MessagePack;
using Oculus.Interaction;
using UnityEngine;

namespace SMJV
{
    [RequireComponent(typeof(MujocoSceneReceiver))]
    public class ControllerPublisher : MonoBehaviour
    {
        [SerializeField] private RayInteractor rightRayInteractor;
        [SerializeField] private Transform simRoot;
        [SerializeField] private InfoWindow infoWindow;
        [SerializeField, Range(MinMul, MaxMul)] private float translationMultiplier = 1.0f;

        private const float Step = 0.1f;
        private const float MinMul = 0.1f;
        private const float MaxMul = 10.0f;
        private const float HighThresh = 0.6f;
        private const float LowThresh = 0.3f;
        private const int SettingCount = 1;

        private MujocoSceneReceiver _receiver;
        private bool _stickLeftLatched, _stickRightLatched, _stickUpLatched, _stickDownLatched;
        private int _selectedSetting;

        public float TranslationMultiplier => translationMultiplier;
        public int SelectedSetting => _selectedSetting;

        void Awake()
        {
            _receiver = GetComponent<MujocoSceneReceiver>();
        }

        void Update()
        {
            if (infoWindow != null && infoWindow.IsVisible)
                HandleDebugStick();

            var rightHand = SampleHand(OVRInput.Controller.RTouch);
            if (IsRightTriggerConsumedBySdk())
                rightHand["index_trigger"] = 0f;

            var payload = new Dictionary<string, object>
            {
                ["left"]  = SampleHand(OVRInput.Controller.LTouch),
                ["right"] = rightHand,
            };

            var data = MessagePackSerializer.Serialize(payload,
                MessagePack.Resolvers.ContractlessStandardResolver.Options);
            var envelope = new MujocoSceneReceiver.Envelope { type = "input", data = data };
            var bytes = MessagePackSerializer.Serialize(envelope);
            _receiver.TryBroadcast(bytes);
        }

        private bool IsRightTriggerConsumedBySdk()
        {
            if (rightRayInteractor != null)
            {
                var s = rightRayInteractor.State;
                if (s == InteractorState.Hover || s == InteractorState.Select) return true;
            }
            return false;
        }

        private void HandleDebugStick()
        {
            Vector2 s = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);

            if (!_stickRightLatched && s.x > HighThresh) { _stickRightLatched = true; AdjustSelected(+Step); }
            else if (_stickRightLatched && s.x < LowThresh) _stickRightLatched = false;

            if (!_stickLeftLatched && s.x < -HighThresh) { _stickLeftLatched = true; AdjustSelected(-Step); }
            else if (_stickLeftLatched && s.x > -LowThresh) _stickLeftLatched = false;

            if (!_stickUpLatched && s.y > HighThresh) { _stickUpLatched = true; MoveCursor(-1); }
            else if (_stickUpLatched && s.y < LowThresh) _stickUpLatched = false;

            if (!_stickDownLatched && s.y < -HighThresh) { _stickDownLatched = true; MoveCursor(+1); }
            else if (_stickDownLatched && s.y > -LowThresh) _stickDownLatched = false;
        }

        private void AdjustSelected(float delta)
        {
            if (_selectedSetting == 0) SetMultiplier(translationMultiplier + delta);
        }

        private void MoveCursor(int delta)
        {
            int next = Mathf.Clamp(_selectedSetting + delta, 0, SettingCount - 1);
            if (next == _selectedSetting) return;
            _selectedSetting = next;
            _receiver?.RefreshInfoWindow();
        }

        private void SetMultiplier(float v)
        {
            // Snap to clean 0.1 increments so repeated bumps don't drift.
            v = Mathf.Round(v * 10f) / 10f;
            v = Mathf.Clamp(v, MinMul, MaxMul);
            if (Mathf.Approximately(v, translationMultiplier)) return;
            translationMultiplier = v;
            _receiver?.RefreshInfoWindow();
        }

        // OVRInput pose is in tracking space (Unity Y-up). We first express it in
        // simRoot's local frame so that (a) positions are relative to the placed scene
        // origin and (b) any rotation of simRoot (e.g. face-to-face orientation) is
        // automatically absorbed. Then we invert SimPublisher's MuJoCo→Unity transform:
        //   publish_state sends Unity xyzw = [M_qy, -M_qz, -M_qx, M_qw]
        //   so the inverse is:  MuJoCo wxyz = [q.w, -q.z, q.x, -q.y]
        private Dictionary<string, object> SampleHand(OVRInput.Controller hand)
        {
            Vector3 pWorld = OVRInput.GetLocalControllerPosition(hand);
            Quaternion qWorld = OVRInput.GetLocalControllerRotation(hand);

            // Express in simRoot local frame (identity if simRoot not assigned).
            Vector3 p = simRoot != null ? simRoot.InverseTransformPoint(pWorld) : pWorld;
            p *= translationMultiplier;
            Quaternion q = simRoot != null ? Quaternion.Inverse(simRoot.rotation) * qWorld : qWorld;

            Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, hand);

            bool suppressStick = hand == OVRInput.Controller.RTouch
                                 && infoWindow != null && infoWindow.IsVisible;
            Vector2 reportedStick = suppressStick ? Vector2.zero : stick;
            bool reportedStickClick = !suppressStick &&
                OVRInput.Get(OVRInput.Button.PrimaryThumbstick, hand);

            var dict = new Dictionary<string, object>
            {
                ["pos"] = new[] { p.z, -p.x, p.y },
                ["rot"] = new[] { q.w, -q.z, q.x, -q.y },  // MuJoCo [w, x, y, z]
                ["index_trigger"] = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, hand),
                ["hand_trigger"]  = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, hand),
                ["thumbstick"] = new[] { reportedStick.x, reportedStick.y },
                ["thumbstick_click"] = reportedStickClick,
            };
            if (hand == OVRInput.Controller.RTouch)
            {
                dict["a"] = OVRInput.Get(OVRInput.Button.One, hand);
                dict["b"] = OVRInput.Get(OVRInput.Button.Two, hand);
            }
            else if (hand == OVRInput.Controller.LTouch)
            {
                dict["x"] = OVRInput.Get(OVRInput.Button.One, hand);
                dict["y"] = OVRInput.Get(OVRInput.Button.Two, hand);
            }
            return dict;
        }
    }
}
