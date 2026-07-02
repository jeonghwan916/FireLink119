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
        [SerializeField] private bool _logDebug;

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
                _socket.selectExited.AddListener(OnSocketExited);
            }
        }

        private void OnDisable()
        {
            if (_socket != null)
            {
                _socket.selectEntered.RemoveListener(OnSocketEntered);
                _socket.selectExited.RemoveListener(OnSocketExited);
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
            if (_socket == null || _safetyPin == null)
            {
                return;
            }

            if (_socket.hasSelection)
            {
                Log("skip restore because socket already has selection.");
                return;
            }

            Transform attach = _socket.attachTransform != null
                ? _socket.attachTransform
                : _socket.transform;

            _safetyPin.transform.SetPositionAndRotation(attach.position, attach.rotation);

            if (_safetyPinRigidbody != null)
            {
                _safetyPinRigidbody.linearVelocity = Vector3.zero;
                _safetyPinRigidbody.angularVelocity = Vector3.zero;
            }

            _socket.socketActive = true;

            Log($"restored safety pin to socket. pin={_safetyPin.name}");
        }

        private void OnSocketEntered(SelectEnterEventArgs args)
        {
            Log($"socket entered interactable={args.interactableObject.transform.name}");
        }

        private void OnSocketExited(SelectExitEventArgs args)
        {
            Log($"socket exited interactable={args.interactableObject.transform.name}");
        }

        private void Log(string message)
        {
            if (!_logDebug)
            {
                return;
            }

            Debug.Log($"[SafetyPinSocketInitializer] {message}");
        }
    }
}
