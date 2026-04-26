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

        private Transform _originalParent;

        void Update()
        {
            if (simRoot == null || leftControllerAnchor == null) return;

            float trigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.LTouch);

            if (IsGrabbing)
            {
                if (trigger < releaseThreshold) Release();
                return;
            }

            if (trigger >= grabThreshold) Grab();
        }

        private void Grab()
        {
            _originalParent = simRoot.parent;
            simRoot.SetParent(leftControllerAnchor, worldPositionStays: true);
            IsGrabbing = true;
        }

        private void Release()
        {
            simRoot.SetParent(_originalParent, worldPositionStays: true);
            IsGrabbing = false;
        }
    }
}
