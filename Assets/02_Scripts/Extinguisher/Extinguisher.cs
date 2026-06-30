using FireLink119.Fire;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class Extinguisher : MonoBehaviour
{
    [Header("Particle")]
    [SerializeField] private ParticleSystem _smokeParticle;

    [Header("Raycast")]
    [SerializeField] private Transform _rayOrigin;
    [SerializeField] private float _range = 5f;
    [SerializeField] private LayerMask _fireLayer;

    private XRGrabInteractable _grabInteractable;
    private bool isFiring = false;

    private void Awake()
    {
        _grabInteractable = GetComponent<XRGrabInteractable>();

        if (_rayOrigin == null)
            _rayOrigin = transform;
    }

    private void OnEnable()
    {
        _grabInteractable.activated.AddListener(OnFireStart);
        _grabInteractable.deactivated.AddListener(OnFireEnd);
    }

    private void OnDisable()
    {
        _grabInteractable.activated.RemoveListener(OnFireStart);
        _grabInteractable.deactivated.RemoveListener(OnFireEnd);
    }

    private void OnFireStart(ActivateEventArgs args)
    {
        if (_smokeParticle != null && !isFiring)
            _smokeParticle.Play();

        isFiring = true;
    }

    private void OnFireEnd(DeactivateEventArgs args)
    {
        if (_smokeParticle != null && isFiring)
            _smokeParticle.Stop();

        isFiring = false;
    }

    private void Update()
    {
        Debug.DrawRay(_rayOrigin.position, _rayOrigin.forward * _range, Color.red);

        if (!isFiring) return;

        if (Physics.Raycast(_rayOrigin.position, _rayOrigin.forward, out RaycastHit hit, _range, _fireLayer, QueryTriggerInteraction.Collide))
        {
            FireObject fire = hit.collider.GetComponentInParent<FireObject>();

            if (fire != null)
            {
                fire.TakeExtinguish(Time.deltaTime);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Transform origin = _rayOrigin != null ? _rayOrigin : transform;

        Gizmos.color = Color.red;
        Gizmos.DrawLine(origin.position, origin.position + origin.forward * _range);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(origin.position, 0.05f);
        Gizmos.DrawWireSphere(origin.position + origin.forward * _range, 0.08f);
    }
}