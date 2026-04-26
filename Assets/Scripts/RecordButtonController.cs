using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SMJV
{
    public class RecordButtonController : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private Color idleColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        [SerializeField] private Color recordingColor = new Color(0.85f, 0.15f, 0.15f, 1f);
        [SerializeField] private string idleText = "Record";
        [SerializeField] private string recordingText = "recording…";

        public bool IsRecording { get; private set; }

        void Awake()
        {
            Apply();
        }

        public void Toggle()
        {
            IsRecording = !IsRecording;
            Apply();
        }

        private void Apply()
        {
            if (background != null) background.color = IsRecording ? recordingColor : idleColor;
            if (label != null) label.text = IsRecording ? recordingText : idleText;
        }
    }
}
