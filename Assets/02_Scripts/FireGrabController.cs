using FireLink119.Fire;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRGrabInteractable))]
[RequireComponent(typeof(Rigidbody))]
public sealed class FireGrabController : MonoBehaviour
{
    [Header("Fire")]
    [SerializeField] private FireObject _fireObject;

    [Header("Grab")]
    [SerializeField] private bool _disableGrabWhileBurning = true;

    private XRGrabInteractable _grabInteractable;
    private Rigidbody _rigidbody;

    private void Awake()
    {
        _grabInteractable = GetComponent<XRGrabInteractable>();
        _rigidbody = GetComponent<Rigidbody>();

        if (_fireObject == null)
        {
            _fireObject = GetComponentInChildren<FireObject>(true);
        }
    }

    private void OnEnable()
    {
        if (_fireObject != null)
        {
            _fireObject.OnExtinguished += HandleFireExtinguished;
        }

        SetBurningState(true);
    }

    private void OnDisable()
    {
        if (_fireObject != null)
        {
            _fireObject.OnExtinguished -= HandleFireExtinguished;
        }
    }

    private void HandleFireExtinguished()
    {
        SetBurningState(false);
    }

    private void SetBurningState(bool isBurning)
    {
        bool canGrab = !isBurning || !_disableGrabWhileBurning;

        if (_grabInteractable != null)
        {
            _grabInteractable.enabled = canGrab;
        }

        if (_rigidbody != null)
        {
            _rigidbody.isKinematic = isBurning;
        }
    }
}