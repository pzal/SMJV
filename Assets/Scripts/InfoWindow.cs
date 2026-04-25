using TMPro;
using UnityEngine;

namespace SMJV
{
    public class InfoWindow : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI label;

        public void SetDisplay(string labelText, string value)
        {
            if (label == null) return;
            label.text = string.IsNullOrEmpty(labelText) ? value : $"{labelText}\n{value}";
        }
    }
}
