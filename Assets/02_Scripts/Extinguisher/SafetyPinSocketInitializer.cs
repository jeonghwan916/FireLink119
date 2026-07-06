using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FireLink119.Extinguisher
{
    [RequireComponent(typeof(XRSocketInteractor))]
    public class SafetyPinSocketInitializer : MonoBehaviour
    {
        [SerializeField] private XRSocketInteractor _socket;
        [SerializeField] private XRGrabInteractable _safetyPin;
        [SerializeField] private int _restoreFrameCount = 2;
        [SerializeField] private bool _detachFromExtinguisherOnHandGrab = true;

        private Rigidbody _safetyPinRigidbody;

        private void Awake()
        {
            if (_socket == null)
            {
                _socket = GetComponent<XRSocketInteractor>();
            }

            if (_safetyPin != null)
            {
                _safetyPinRigidbody = _safetyPin.GetComponent<Rigidbody>();
            }
        }

        private void OnEnable()
        {
            if (_socket != null)
            {
                _socket.selectEntered.AddListener(OnSocketEntered);
            }

            if (_safetyPin != null)
            {
                _safetyPin.selectEntered.AddListener(OnSafetyPinSelected);
                _safetyPin.selectExited.AddListener(OnSafetyPinDeselected);
            }
        }

        private void OnDisable()
        {
            if (_socket != null)
            {
                _socket.selectEntered.RemoveListener(OnSocketEntered);
            }

            if (_safetyPin != null)
            {
                _safetyPin.selectEntered.RemoveListener(OnSafetyPinSelected);
                _safetyPin.selectExited.RemoveListener(OnSafetyPinDeselected);
            }
        }

        private void Start()
        {
            StartCoroutine(RestoreSocketAfterSpawn());
        }

        private IEnumerator RestoreSocketAfterSpawn()
        {
            int frames = Mathf.Max(_restoreFrameCount, 1);
            for (int i = 0; i < frames; i++)
            {
                yield return null;
            }

            for (int i = 0; i < frames; i++)
            {
                RestoreSafetyPinToSocket();
                yield return null;
            }
        }

        private void RestoreSafetyPinToSocket()
        {
            if (_socket == null || _safetyPin == null || _socket.hasSelection)
            {
                return;
            }

            Transform attach = _socket.attachTransform != null
                ? _socket.attachTransform
                : _socket.transform;

            _safetyPin.transform.SetPositionAndRotation(attach.position, attach.rotation);
            SetSafetyPinKinematic();
            _socket.socketActive = true;
        }

        private void OnSocketEntered(SelectEnterEventArgs args)
        {
            if (args.interactableObject.transform == _safetyPin.transform)
            {
                SetSafetyPinKinematic();
            }
        }

        private void OnSafetyPinSelected(SelectEnterEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor)
            {
                SetSafetyPinKinematic();
                return;
            }

            if (_detachFromExtinguisherOnHandGrab)
            {
                _safetyPin.transform.SetParent(null, true);
            }

            SetSafetyPinHeld();
        }

        private void OnSafetyPinDeselected(SelectExitEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor)
            {
                return;
            }

            if (_detachFromExtinguisherOnHandGrab)
            {
                _safetyPin.transform.SetParent(null, true);
                StartCoroutine(DetachAfterSelectionEnds());
            }

            SetSafetyPinDynamic();
        }

        private IEnumerator DetachAfterSelectionEnds()
        {
            yield return null;

            if (_safetyPin != null)
            {
                _safetyPin.transform.SetParent(null, true);
            }
        }

        private void SetSafetyPinKinematic()
        {
            if (_safetyPinRigidbody == null)
            {
                return;
            }

            _safetyPinRigidbody.linearVelocity = Vector3.zero;
            _safetyPinRigidbody.angularVelocity = Vector3.zero;
            _safetyPinRigidbody.useGravity = false;
            _safetyPinRigidbody.isKinematic = true;
        }

        private void SetSafetyPinDynamic()
        {
            if (_safetyPinRigidbody == null)
            {
                return;
            }

            _safetyPinRigidbody.isKinematic = false;
            _safetyPinRigidbody.useGravity = true;
            _safetyPinRigidbody.WakeUp();
        }

        private void SetSafetyPinHeld()
        {
            if (_safetyPinRigidbody == null)
            {
                return;
            }

            _safetyPinRigidbody.linearVelocity = Vector3.zero;
            _safetyPinRigidbody.angularVelocity = Vector3.zero;
            _safetyPinRigidbody.isKinematic = false;
            _safetyPinRigidbody.useGravity = false;
            _safetyPinRigidbody.WakeUp();
        }
    }
}
