using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FireLink119.Extinguisher
{
    public class SafetyPinGrabCondition : MonoBehaviour, IXRSelectFilter
    {
        [SerializeField] private Extinguisher _extinguisher;
        [SerializeField] private bool _allowSocketSelectionBeforePulled = true;
        [SerializeField] private bool _blockSocketSelectionAfterPulled = true;

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
            if (!_allowSocketSelectionBeforePulled)
            {
                return false;
            }

            if (_extinguisher == null || !_extinguisher.IsNetworkReady)
            {
                return true;
            }

            return !_blockSocketSelectionAfterPulled || !_extinguisher.NetworkIsSafetyPinPulled;
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