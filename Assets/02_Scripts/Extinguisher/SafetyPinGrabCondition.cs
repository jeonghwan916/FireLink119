using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FireLink119.Extinguisher
{
    public class SafetyPinGrabCondition : MonoBehaviour, IXRSelectFilter
    {
        [SerializeField] private Extinguisher _extinguisher;

        public bool canProcess => isActiveAndEnabled;

        private void Awake()
        {
            if (_extinguisher == null)
            {
                _extinguisher = GetComponentInParent<Extinguisher>();
            }
        }

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (interactor is XRSocketInteractor)
            {
                return CanSelectBySocket();
            }

            return CanSelectByLocalHolder(interactor, interactable);
        }

        private bool CanSelectBySocket()
        {
            if (_extinguisher == null || !_extinguisher.IsNetworkReady)
            {
                return true;
            }

            return !_extinguisher.NetworkIsSafetyPinPulled;
        }

        private bool CanSelectByLocalHolder(
            IXRSelectInteractor interactor,
            IXRSelectInteractable interactable)
        {
            if (_extinguisher == null || !_extinguisher.IsNetworkReady)
            {
                return false;
            }

            if (!_extinguisher.IsHeldByLocalPlayer)
            {
                return false;
            }

            if (!_extinguisher.NetworkIsSafetyPinPulled)
            {
                return true;
            }

            return IsCurrentlySelectedBy(interactor, interactable);
        }

        private static bool IsCurrentlySelectedBy(
            IXRSelectInteractor interactor,
            IXRSelectInteractable interactable)
        {
            foreach (IXRSelectInteractor selectingInteractor in interactable.interactorsSelecting)
            {
                if (selectingInteractor == interactor)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
