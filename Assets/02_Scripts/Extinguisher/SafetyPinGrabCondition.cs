using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FireLink119.Extinguisher
{
    public class SafetyPinGrabCondition : MonoBehaviour, IXRSelectFilter
    {
        [Header("Network State Source")]
        [SerializeField] private Extinguisher _extinguisher;

        [Header("Fallback")]
        [SerializeField] private XRGrabInteractable _extinguisherGrab;

        [Header("Socket")]
        [SerializeField] private bool _allowSocketSelectionBeforePulled = true;
        [SerializeField] private bool _blockSocketSelectionAfterPulled = true;

        public bool canProcess => isActiveAndEnabled;

        private void Awake()
        {
            if (_extinguisher == null)
            {
                _extinguisher = GetComponentInParent<Extinguisher>();
            }

            if (_extinguisherGrab == null && _extinguisher != null)
            {
                _extinguisherGrab = _extinguisher.GetComponent<XRGrabInteractable>();
            }
        }

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (_extinguisher == null)
            {
                return CanGrabByLocalFallback(interactor);
            }

            if (interactor is XRSocketInteractor)
            {
                return CanSelectBySocket();
            }

            return CanGrabByLocalPlayer(interactor);
        }

        private bool CanSelectBySocket()
        {
            if (!_allowSocketSelectionBeforePulled)
            {
                return false;
            }

            if (!_extinguisher.IsNetworkReady)
            {
                return true;
            }

            if (_blockSocketSelectionAfterPulled && _extinguisher.NetworkIsSafetyPinPulled)
            {
                return false;
            }

            return true;
        }

        private bool CanGrabByLocalPlayer(IXRSelectInteractor interactor)
        {
            if (!_extinguisher.IsNetworkReady)
            {
                return CanGrabByLocalFallback(interactor);
            }

            if (_extinguisher.NetworkIsSafetyPinPulled)
            {
                return false;
            }

            return _extinguisher.IsHeldByLocalPlayer;
        }

        private bool CanGrabByLocalFallback(IXRSelectInteractor interactor)
        {
            if (interactor is XRSocketInteractor)
            {
                return true;
            }

            return _extinguisherGrab != null && _extinguisherGrab.isSelected;
        }
    }
}