using UnityEngine;

namespace SMJV
{
    public class SceneRootGrabber : MonoBehaviour
    {
        [SerializeField] private Transform simRoot;
        [SerializeField] private Transform leftControllerAnchor;
        [SerializeField] private float grabThreshold = 0.7f;
        [SerializeField] private float releaseThreshold = 0.3f;

        public bool IsGrabbing { get; private set; }

        // Offset from controller to simRoot at grab time (in controller's yaw-only frame)
        private Vector3 _offsetPos;
        private float _offsetYaw;

        void Update()
        {
            if (simRoot == null || leftControllerAnchor == null) return;

            float trigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.LTouch);

            if (IsGrabbing)
            {
                if (trigger < releaseThreshold)
                    IsGrabbing = false;
                else
                    ApplyGrab();
                return;
            }

            if (trigger >= grabThreshold)
                StartGrab();
        }

        private void StartGrab()
        {
            float controllerYaw = GetYaw(leftControllerAnchor.rotation);
            Quaternion yawOnly = Quaternion.Euler(0f, controllerYaw, 0f);
            // Store offset in yaw-only local space so drag feels natural
            _offsetPos = Quaternion.Inverse(yawOnly) * (simRoot.position - leftControllerAnchor.position);
            _offsetYaw = simRoot.eulerAngles.y - controllerYaw;
            IsGrabbing = true;
        }

        private void ApplyGrab()
        {
            float controllerYaw = GetYaw(leftControllerAnchor.rotation);
            Quaternion yawOnly = Quaternion.Euler(0f, controllerYaw, 0f);
            simRoot.position = leftControllerAnchor.position + yawOnly * _offsetPos;
            simRoot.rotation = Quaternion.Euler(0f, controllerYaw + _offsetYaw, 0f);
        }

        private static float GetYaw(Quaternion q) => q.eulerAngles.y;
    }
}
