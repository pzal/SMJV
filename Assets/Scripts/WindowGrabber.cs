using UnityEngine;

namespace SMJV
{
    public class WindowGrabber : MonoBehaviour
    {
        [SerializeField] private Collider grabHandle;
        [SerializeField] private Transform rightControllerAnchor;
        [SerializeField] private float grabThreshold = 0.7f;
        [SerializeField] private float releaseThreshold = 0.3f;
        [SerializeField] private float rayLength = 5f;
        [SerializeField] private Renderer handleRenderer;
        [SerializeField] private Color hoverColor = new Color(0.4f, 0.8f, 1f, 1f);
        [SerializeField] private Color idleColor = new Color(1f, 1f, 1f, 0.3f);

        public bool IsGrabbing { get; private set; }

        private Transform _originalParent;

        void Awake()
        {
            _originalParent = transform.parent;
        }

        void Update()
        {
            if (rightControllerAnchor == null || grabHandle == null) return;

            float trigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch);

            if (IsGrabbing)
            {
                if (trigger < releaseThreshold) Release();
                return;
            }

            bool hovering = Raycast(out _);
            if (handleRenderer != null)
                handleRenderer.material.color = hovering ? hoverColor : idleColor;

            if (hovering && trigger >= grabThreshold) Grab();
        }

        private bool Raycast(out RaycastHit hit)
        {
            var ray = new Ray(rightControllerAnchor.position, rightControllerAnchor.forward);
            // QueryTriggerInteraction.Collide so we don't have to set the rim collider as solid.
            if (Physics.Raycast(ray, out hit, rayLength, ~0, QueryTriggerInteraction.Collide))
                return hit.collider == grabHandle;
            return false;
        }

        private void Grab()
        {
            transform.SetParent(rightControllerAnchor, worldPositionStays: true);
            IsGrabbing = true;
        }

        private void Release()
        {
            transform.SetParent(_originalParent, worldPositionStays: true);
            IsGrabbing = false;
        }
    }
}
