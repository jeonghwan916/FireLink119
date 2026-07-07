using FireLink119.Fire;
using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace FireLink119.Interaction
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkTransform))]
    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class FireGrabController : MonoBehaviour
    {
        [Header("Fire")] [SerializeField] private FireObject _fireObject;

        [Header("Grab")] [SerializeField] private bool _disableGrabWhileBurning = true;

        private NetworkObject _networkObject;
        private XRGrabInteractable _grabInteractable;
        private Rigidbody _rigidbody;
        private bool _hasExtinguishedEvent;

        private void Awake()
        {
            _networkObject = GetComponent<NetworkObject>();
            _grabInteractable = GetComponent<XRGrabInteractable>();
            _rigidbody = GetComponent<Rigidbody>();

            if (_fireObject == null)
            {
                _fireObject = GetComponentInChildren<FireObject>(true);
            }
        }

        private void OnEnable()
        {
            _grabInteractable.selectEntered.AddListener(OnGrabbed);
            _hasExtinguishedEvent = IsFireExtinguishedFromNetwork();

            if (_fireObject != null)
            {
                _fireObject.OnExtinguished += HandleFireExtinguished;
            }

            ApplyInteractionState();
        }

        private void OnDisable()
        {
            _grabInteractable.selectEntered.RemoveListener(OnGrabbed);

            if (_fireObject != null)
            {
                _fireObject.OnExtinguished -= HandleFireExtinguished;
            }
        }

        private void Update()
        {
            _hasExtinguishedEvent |= IsFireExtinguishedFromNetwork();
            ApplyInteractionState();
        }

        private void HandleFireExtinguished()
        {
            _hasExtinguishedEvent = true;
            ApplyInteractionState();
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            if (CanGrabFromFireState() && !_networkObject.HasStateAuthority)
            {
                _networkObject.RequestStateAuthority();
            }
        }

        private void ApplyInteractionState()
        {
            bool canGrab = CanGrabFromFireState();

            _grabInteractable.enabled = canGrab;
            _rigidbody.isKinematic = !canGrab;
        }

        private bool CanGrabFromFireState()
        {
            return !_disableGrabWhileBurning || _hasExtinguishedEvent || IsFireExtinguishedFromNetwork();
        }

        private bool IsFireExtinguishedFromNetwork()
        {
            return _fireObject != null && _fireObject.NetworkIsExtinguished;
        }
    }
}
