using FireLink119.Fire;
using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FireLink119.Interaction
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkTransform))]
    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class FireGrabController : MonoBehaviour, IXRSelectFilter
    {
        [Header("Fire")] [SerializeField] private FireObject _fireObject;

        [Header("Grab")] [SerializeField] private bool _disableGrabWhileBurning = true;

        private NetworkObject _networkObject;
        private XRGrabInteractable _grabInteractable;
        private Rigidbody _rigidbody;
        private bool _hasExtinguishedEvent;
        private bool _isSelected;

        public bool canProcess => isActiveAndEnabled;

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
            _grabInteractable.selectExited.AddListener(OnReleased);
            _grabInteractable.selectFilters.Add(this);
            _hasExtinguishedEvent = false;

            if (_fireObject != null)
            {
                _fireObject.OnExtinguished += HandleFireExtinguished;
            }

            ApplyInteractionState();
        }

        private void OnDisable()
        {
            _grabInteractable.selectFilters.Remove(this);
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
            ApplyInteractionState();
        }

        private void HandleFireExtinguished()
        {
            _hasExtinguishedEvent = true;
            ApplyInteractionState();
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            _isSelected = true;
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            _isSelected = false;

            if (_networkObject.HasStateAuthority)
            {
                _networkObject.ReleaseStateAuthority();
            }
        }

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (!CanGrabFromFireState())
            {
                return false;
            }

            if (_networkObject.HasStateAuthority)
            {
                return true;
            }

            _networkObject.RequestStateAuthority();
            return false;
        }

        private void ApplyInteractionState()
        {
            bool canGrab = CanGrabFromFireState();

            _grabInteractable.enabled = _isSelected || canGrab;
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
