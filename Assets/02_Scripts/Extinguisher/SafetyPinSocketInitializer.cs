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
        }

        private void OnDisable()
        {
            if (_socket != null)
            {
                _socket.selectEntered.RemoveListener(OnSocketEntered);
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

            RestoreSafetyPinToSocket();
        }

        private void RestoreSafetyPinToSocket()
        {
            if (_socket == null || _safetyPin == null)
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
            if (_safetyPin != null && args.interactableObject.transform == _safetyPin.transform)
            {
                SetSafetyPinKinematic();
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
    }
}
