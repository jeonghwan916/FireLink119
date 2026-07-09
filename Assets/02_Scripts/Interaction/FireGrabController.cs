using System.Collections;
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
        private bool _hasActivatedPhysics;
        private bool _hasAppliedGrabAvailability;
        private bool _lastGrabAvailability;
        private bool _isSelected;

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
            _grabInteractable.hoverEntered.AddListener(OnHoverEntered);
            _grabInteractable.selectEntered.AddListener(OnGrabbed);
            _grabInteractable.selectExited.AddListener(OnReleased);
            _hasExtinguishedEvent = false;
            _hasAppliedGrabAvailability = false;

            if (_fireObject != null)
            {
                _fireObject.OnExtinguished += HandleFireExtinguished;
            }

            EnsurePhysicsLockedBeforeFirstGrab();
            ApplyGrabAvailability();
        }

        private void OnDisable()
        {
            _grabInteractable.hoverEntered.RemoveListener(OnHoverEntered);
            _grabInteractable.selectEntered.RemoveListener(OnGrabbed);
            _grabInteractable.selectExited.RemoveListener(OnReleased);

            if (_fireObject != null)
            {
                _fireObject.OnExtinguished -= HandleFireExtinguished;
            }
        }

        private void Update()
        {
            _hasExtinguishedEvent |= IsFireExtinguishedFromNetwork();

            if (_isSelected)
            {
                RequestStateAuthorityIfCanGrab();
            }

            ApplyGrabAvailability();
        }

        private void HandleFireExtinguished()
        {
            _hasExtinguishedEvent = true;
            ApplyGrabAvailability();
        }

        private void OnHoverEntered(HoverEnterEventArgs args)
        {
            RequestStateAuthorityIfCanGrab();
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            _isSelected = true;

            if (CanGrabFromFireState())
            {
                ActivatePhysicsOnce();
            }

            RequestStateAuthorityIfCanGrab();
            ApplyGrabAvailability();
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            _isSelected = false;
            ApplyGrabAvailability();
            ApplyActivatedPhysicsState();
            StartCoroutine(ApplyActivatedPhysicsStateAfterRelease());
        }

        private void RequestStateAuthorityIfCanGrab()
        {
            if (CanGrabFromFireState() && !_networkObject.HasStateAuthority)
            {
                _networkObject.RequestStateAuthority();
            }
        }

        private void ApplyGrabAvailability()
        {
            bool canGrab = CanGrabFromFireState();
            bool grabAvailability = _isSelected || canGrab;

            if (_hasAppliedGrabAvailability && _lastGrabAvailability == grabAvailability)
            {
                return;
            }

            _grabInteractable.enabled = grabAvailability;
            _lastGrabAvailability = grabAvailability;
            _hasAppliedGrabAvailability = true;
        }

        private void EnsurePhysicsLockedBeforeFirstGrab()
        {
            if (!_hasActivatedPhysics)
            {
                _rigidbody.isKinematic = true;
            }
        }

        private void ActivatePhysicsOnce()
        {
            if (_hasActivatedPhysics)
            {
                return;
            }

            _hasActivatedPhysics = true;
            ApplyActivatedPhysicsState();
        }

        private IEnumerator ApplyActivatedPhysicsStateAfterRelease()
        {
            yield return null;
            ApplyActivatedPhysicsState();
        }

        private void ApplyActivatedPhysicsState()
        {
            if (!_hasActivatedPhysics)
            {
                return;
            }

            _rigidbody.isKinematic = false;
            _rigidbody.WakeUp();
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
