using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FireLink119.Extinguisher
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(XRGrabInteractable))]
    public class SafetyPinPullController : MonoBehaviour
    {
        [SerializeField] private XRGrabInteractable _safetyPin;
        [SerializeField] private Extinguisher _extinguisher;
        [SerializeField] private XRSocketInteractor _socket;

        private Rigidbody _rigidbody;
        private bool _detached;

        private void Awake()
        {
            if (_safetyPin == null)
            {
                _safetyPin = GetComponent<XRGrabInteractable>();
            }

            _rigidbody = GetComponent<Rigidbody>();

            if (_extinguisher == null)
            {
                _extinguisher = GetComponentInParent<Extinguisher>();
            }

            if (_socket == null && _extinguisher != null)
            {
                _socket = _extinguisher.GetComponentInChildren<XRSocketInteractor>(true);
            }
        }

        private void OnEnable()
        {
            _safetyPin.selectEntered.AddListener(OnSelectEntered);
            _safetyPin.selectExited.AddListener(OnSelectExited);
        }

        private void OnDisable()
        {
            _safetyPin.selectEntered.RemoveListener(OnSelectEntered);
            _safetyPin.selectExited.RemoveListener(OnSelectExited);
        }

        private void LateUpdate()
        {
            if (_extinguisher != null && _extinguisher.NetworkIsSafetyPinPulled)
            {
                DetachFromExtinguisher(keepKinematic: IsSelectedByHand());
            }
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor)
            {
                SetKinematic();
                return;
            }

            _extinguisher?.RequestPullSafetyPin();
            DetachFromExtinguisher(keepKinematic: true);
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor)
            {
                return;
            }

            if (_detached || (_extinguisher != null && _extinguisher.NetworkIsSafetyPinPulled))
            {
                SetDynamic();
            }
        }

        private void DetachFromExtinguisher(bool keepKinematic)
        {
            if (!_detached)
            {
                ForceSocketSelectExit();

                if (_socket != null)
                {
                    _socket.socketActive = false;
                }
            }

            if (transform.parent != null)
            {
                transform.SetParent(null, worldPositionStays: true);
            }

            _detached = true;

            if (keepKinematic)
            {
                SetKinematic();
            }
            else
            {
                SetDynamic();
            }
        }

        private void ForceSocketSelectExit()
        {
            if (_socket == null || _safetyPin == null || !_socket.hasSelection || _socket.interactionManager == null)
            {
                return;
            }

            _socket.interactionManager.SelectExit(
                (IXRSelectInteractor)_socket,
                (IXRSelectInteractable)_safetyPin);
        }

        private bool IsSelectedByHand()
        {
            if (_safetyPin == null)
            {
                return false;
            }

            foreach (IXRSelectInteractor interactor in _safetyPin.interactorsSelecting)
            {
                if (interactor is not XRSocketInteractor)
                {
                    return true;
                }
            }

            return false;
        }

        private void SetKinematic()
        {
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.useGravity = false;
            _rigidbody.isKinematic = true;
        }

        private void SetDynamic()
        {
            _rigidbody.isKinematic = false;
            _rigidbody.useGravity = true;
            _rigidbody.WakeUp();
        }
    }
}
