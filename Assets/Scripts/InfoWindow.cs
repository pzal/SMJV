using TMPro;
using UnityEngine;

namespace SMJV
{
    public class InfoWindow : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI label;

        private string _statusLine = "";
        private string _payloadText = "";

        public void SetStatus(string status)
        {
            _statusLine = status ?? "";
            Render();
        }

        public void SetDisplay(string labelText, string value)
        {
            _payloadText = string.IsNullOrEmpty(labelText)
                ? (value ?? "")
                : $"{labelText}\n{value}";
            Render();
        }

        private void Render()
        {
            if (label == null) return;
            if (string.IsNullOrEmpty(_statusLine))
                label.text = _payloadText;
            else if (string.IsNullOrEmpty(_payloadText))
                label.text = _statusLine;
            else
                label.text = $"{_statusLine}\n\n{_payloadText}";
        }
    }
}
