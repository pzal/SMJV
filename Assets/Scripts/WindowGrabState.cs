using Oculus.Interaction;
using UnityEngine;

namespace SMJV
{
    public class WindowGrabState : MonoBehaviour
    {
        [SerializeField] private Grabbable grabbable;

        public bool IsGrabbing => grabbable != null && grabbable.SelectingPointsCount > 0;
    }
}
