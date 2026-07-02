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
            string interactorName = interactor.transform != null
                ? interactor.transform.name
                : "UnknownInteractor";

            string interactableName = interactable.transform != null
                ? interactable.transform.name
                : "UnknownInteractable";

            bool isSocket = interactor is XRSocketInteractor;

            if (_extinguisher == null)
            {
                bool fallbackResult = CanGrabByLocalFallback(interactor);

                Debug.Log(
                    $"[SafetyPinGrabCondition][Process] extinguisher=null " +
                    $"interactor={interactorName} interactable={interactableName} " +
                    $"isSocket={isSocket} result={fallbackResult}");

                return fallbackResult;
            }

            if (isSocket)
            {
                bool socketResult = CanSelectBySocket();

                Debug.Log(
                    $"[SafetyPinGrabCondition][Process] socket " +
                    $"interactor={interactorName} interactable={interactableName} " +
                    $"ready={_extinguisher.IsNetworkReady} " +
                    $"pinPulled={_extinguisher.NetworkIsSafetyPinPulled} " +
                    $"result={socketResult}");

                return socketResult;
            }

            bool result = CanGrabByLocalPlayer(interactor);

            Debug.Log(
                $"[SafetyPinGrabCondition][Process] hand " +
                $"interactor={interactorName} interactable={interactableName} " +
                $"ready={_extinguisher.IsNetworkReady} " +
                $"held={_extinguisher.NetworkIsHeld} " +
                $"heldByLocal={_extinguisher.IsHeldByLocalPlayer} " +
                $"pinPulled={_extinguisher.NetworkIsSafetyPinPulled} " +
                $"result={result}");

            return result;
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