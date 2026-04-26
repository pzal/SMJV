using TMPro;
using UnityEngine;

namespace SMJV
{
    public class InfoWindow : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI label;

        private bool _visible;
        private GameObject _panel;

        void Awake()
        {
            // Keep this MonoBehaviour's GO active so Update() always runs,
            // but hide the visual panel (Canvas child) instead.
            _panel = transform.Find("Canvas")?.gameObject;
            SetPanelVisible(false);
        }

        void Update()
        {
            if (OVRInput.GetDown(OVRInput.Button.Start, OVRInput.Controller.LTouch))
            {
                _visible = !_visible;
                SetPanelVisible(_visible);
            }
        }

        private void SetPanelVisible(bool show)
        {
            if (_panel != null) _panel.SetActive(show);
        }

        public void SetText(string text)
        {
            if (label != null) label.text = text;
        }

        // Called from editor/debug to force visibility without the button
        public void ForceVisible(bool show)
        {
            _visible = show;
            SetPanelVisible(show);
        }
    }
}
