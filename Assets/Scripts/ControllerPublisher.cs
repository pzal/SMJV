using System.Collections.Generic;
using MessagePack;
using UnityEngine;

namespace SMJV
{
    [RequireComponent(typeof(MujocoSceneReceiver))]
    public class ControllerPublisher : MonoBehaviour
    {
        [SerializeField] private WindowGrabber grabber;

        private MujocoSceneReceiver _receiver;

        void Awake()
        {
            _receiver = GetComponent<MujocoSceneReceiver>();
        }

        void Update()
        {
            var rightHand = SampleHand(OVRInput.Controller.RTouch);
            // While the user is grabbing the window with the right index trigger,
            // suppress only that axis so the trigger pull doesn't propagate.
            if (grabber != null && grabber.IsGrabbing)
                rightHand["index_trigger"] = 0f;

            var payload = new Dictionary<string, object>
            {
                ["left"]  = SampleHand(OVRInput.Controller.LTouch),
                ["right"] = rightHand,
                ["A"]     = OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch),
                ["B"]     = OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.RTouch),
                ["X"]     = OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.LTouch),
                ["Y"]     = OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.LTouch),
            };

            var data = MessagePackSerializer.Serialize(payload,
                MessagePack.Resolvers.ContractlessStandardResolver.Options);
            var envelope = new MujocoSceneReceiver.Envelope { type = "input", data = data };
            var bytes = MessagePackSerializer.Serialize(envelope);
            _receiver.TryBroadcast(bytes);
        }

        // OVRInput pose is in tracking space (Unity Y-up). We invert SimPublisher's
        // MuJoCo→Unity transform so consumers receive MuJoCo Z-up directly.
        //   pos_unity  = [-py_m, pz_m, px_m]              → pos_m  = [pz_u, -px_u, py_u]
        //   quat_unity = [qz_m, -qw_m, -qy_m, qx_m] (xyzw) → quat_m = [-qy_u, qw_u, -qz_u, qx_u] (wxyz)
        private static Dictionary<string, object> SampleHand(OVRInput.Controller hand)
        {
            Vector3 p = OVRInput.GetLocalControllerPosition(hand);
            Quaternion q = OVRInput.GetLocalControllerRotation(hand);
            Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, hand);

            return new Dictionary<string, object>
            {
                ["pos"] = new[] { p.z, -p.x, p.y },
                ["rot"] = new[] { -q.y, q.w, -q.z, q.x },
                ["index_trigger"] = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, hand),
                ["hand_trigger"]  = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, hand),
                ["thumbstick"] = new[] { stick.x, stick.y },
                ["thumbstick_click"] = OVRInput.Get(OVRInput.Button.PrimaryThumbstick, hand),
            };
        }
    }
}
