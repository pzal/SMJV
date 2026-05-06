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

        private MujocoSceneReceiver _receiver;

        void Awake()
        {
            _receiver = GetComponent<MujocoSceneReceiver>();
        }

        void Update()
        {
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
            Quaternion q = simRoot != null ? Quaternion.Inverse(simRoot.rotation) * qWorld : qWorld;

            Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, hand);

            var dict = new Dictionary<string, object>
            {
                ["pos"] = new[] { p.z, -p.x, p.y },
                ["rot"] = new[] { q.w, -q.z, q.x, -q.y },  // MuJoCo [w, x, y, z]
                ["index_trigger"] = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, hand),
                ["hand_trigger"]  = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, hand),
                ["thumbstick"] = new[] { stick.x, stick.y },
                ["thumbstick_click"] = OVRInput.Get(OVRInput.Button.PrimaryThumbstick, hand),
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
