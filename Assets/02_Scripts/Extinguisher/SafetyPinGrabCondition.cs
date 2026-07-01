using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FireLink119.Extinguisher
{
    public class SafetyPinGrabCondition : MonoBehaviour, IXRSelectFilter
    {
        [SerializeField] private XRGrabInteractable _extinguisherGrab;

        public bool canProcess => isActiveAndEnabled;

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (interactor is XRSocketInteractor)
                return true;

            return _extinguisherGrab != null && _extinguisherGrab.isSelected;
        }
    }
}