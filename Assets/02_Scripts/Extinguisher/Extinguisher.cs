using FireLink119.Fire;
using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FireLink119.Extinguisher
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkTransform))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(XRGrabInteractable))]
    public class Extinguisher : NetworkBehaviour, IStateAuthorityChanged
    {
        [Header("Particle")]
        [SerializeField] private ParticleSystem _smokeParticle;

        [Header("Raycast")]
        [SerializeField] private Transform _rayOrigin;
        [SerializeField] private float _range = 5f;
        [SerializeField] private LayerMask _fireLayer;

        [Header("Safety Pin")]
        [SerializeField] private XRSocketInteractor _safetyPinSocket;
        [SerializeField] private GameObject[] _safetyPinVisuals;

        [Header("Authority")]
        [SerializeField] private float _grabAuthorityRequestTimeout = 0.75f;

        [Networked] private NetworkBool IsHeld { get; set; }
        [Networked] private PlayerRef HeldBy { get; set; }
        [Networked] private NetworkBool IsSafetyPinPulled { get; set; }
        [Networked] private NetworkBool IsFiring { get; set; }

        public bool IsNetworkReady => _isSpawned && Object != null && Runner != null;
        public bool NetworkIsHeld => IsNetworkReady && IsHeld;
        public bool NetworkIsSafetyPinPulled => IsNetworkReady && IsSafetyPinPulled;
        public bool NetworkIsFiring => IsNetworkReady && IsFiring;
        public bool IsHeldByLocalPlayer => IsNetworkReady && IsHeld && HeldBy == Runner.LocalPlayer;

        private XRGrabInteractable _grabInteractable;
        private Rigidbody _rigidbody;
        private AudioSource _extinguisherSfx;
        private bool _isSpawned;
        private bool _isLocallySelected;
        private bool _pendingGrab;
        private float _pendingGrabStartedTime;
        private bool _lastRenderedFiring;
        private bool _lastRenderedSafetyPinPulled;

        private void Awake()
        {
            _grabInteractable = GetComponent<XRGrabInteractable>();
            _rigidbody = GetComponent<Rigidbody>();
            _extinguisherSfx = GetComponent<AudioSource>();

            if (_rayOrigin == null)
            {
                _rayOrigin = transform;
            }
        }

        private void OnEnable()
        {
            _grabInteractable.selectEntered.AddListener(OnGrabbed);
            _grabInteractable.selectExited.AddListener(OnReleased);
            _grabInteractable.activated.AddListener(OnFireStart);
            _grabInteractable.deactivated.AddListener(OnFireEnd);

            if (_safetyPinSocket != null)
            {
                _safetyPinSocket.selectExited.AddListener(OnSafetyPinSocketExited);
            }
        }

        private void OnDisable()
        {
            _grabInteractable.selectEntered.RemoveListener(OnGrabbed);
            _grabInteractable.selectExited.RemoveListener(OnReleased);
            _grabInteractable.activated.RemoveListener(OnFireStart);
            _grabInteractable.deactivated.RemoveListener(OnFireEnd);

            if (_safetyPinSocket != null)
            {
                _safetyPinSocket.selectExited.RemoveListener(OnSafetyPinSocketExited);
            }
        }

        public override void Spawned()
        {
            _isSpawned = true;

            Debug.Log($"[Extinguisher] Spawned. object={name}, local={Runner.LocalPlayer}, stateAuthority={Object.StateAuthority}, hasStateAuthority={HasStateAuthority}, isMasterClient={Runner.IsSharedModeMasterClient}");

            if (HasStateAuthority)
            {
                IsHeld = false;
                HeldBy = PlayerRef.None;
                IsSafetyPinPulled = false;
                IsFiring = false;
                EnsureReleasedPhysicsState();
            }

            ApplyNetworkState(force: true);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            Debug.Log($"[Extinguisher] Despawned. object={name}, hasState={hasState}");

            _isSpawned = false;
            _pendingGrab = false;
            _isLocallySelected = false;
        }

        public void StateAuthorityChanged()
        {
            Debug.Log($"[Extinguisher] StateAuthorityChanged. object={name}, local={Runner.LocalPlayer}, stateAuthority={Object.StateAuthority}, hasStateAuthority={HasStateAuthority}, pendingGrab={_pendingGrab}, isLocallySelected={_isLocallySelected}, isHeld={IsHeld}, heldBy={HeldBy}");

            if (HasStateAuthority && _pendingGrab)
            {
                _pendingGrab = false;
                SetGrabbed(Runner.LocalPlayer);
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!IsNetworkReady)
            {
                return;
            }

            ClearInvalidPendingGrab();

            if (HasStateAuthority)
            {
                RecoverAbandonedHold();
            }

            if (Runner.IsSharedModeMasterClient && IsFiring)
            {
                TryExtinguishFire();
            }
        }

        public override void Render()
        {
            if (!IsNetworkReady)
            {
                return;
            }

            ApplyNetworkState(force: false);
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            _isLocallySelected = true;
            Debug.Log($"[Extinguisher] OnGrabbed. object={name}, local={Runner.LocalPlayer}, interactor={args.interactorObject.transform.name}, hasStateAuthority={HasStateAuthority}, stateAuthority={Object.StateAuthority}, isHeld={IsHeld}, heldBy={HeldBy}");
            RequestGrabAuthority();
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            Debug.Log($"[Extinguisher] OnReleased. object={name}, local={Runner.LocalPlayer}, interactor={args.interactorObject.transform.name}, hasStateAuthority={HasStateAuthority}, stateAuthority={Object.StateAuthority}, isHeld={IsHeld}, heldBy={HeldBy}, pendingGrab={_pendingGrab}");
            _isLocallySelected = false;
            _pendingGrab = false;
            ReleaseIfHeldByLocalPlayer();
        }

        private void OnSafetyPinSocketExited(SelectExitEventArgs args)
        {
            if (IsHeldByLocalPlayer && HasStateAuthority)
            {
                IsSafetyPinPulled = true;
            }
        }

        private void OnFireStart(ActivateEventArgs args)
        {
            SetFiring(true);
        }

        private void OnFireEnd(DeactivateEventArgs args)
        {
            SetFiring(false);
        }

        private void RequestGrabAuthority()
        {
            if (!IsNetworkReady || IsHeldByOtherPlayer())
            {
                Debug.Log($"[Extinguisher] RequestGrabAuthority blocked. object={name}, isNetworkReady={IsNetworkReady}, local={Runner?.LocalPlayer}, hasStateAuthority={HasStateAuthority}, stateAuthority={Object?.StateAuthority}, isHeld={IsHeld}, heldBy={HeldBy}");
                return;
            }

            _pendingGrab = true;
            _pendingGrabStartedTime = Time.time;

            Debug.Log($"[Extinguisher] RequestGrabAuthority started. object={name}, local={Runner.LocalPlayer}, hasStateAuthority={HasStateAuthority}, stateAuthority={Object.StateAuthority}, objectId={Object.Id}, timeout={_grabAuthorityRequestTimeout}");

            if (HasStateAuthority)
            {
                _pendingGrab = false;
                SetGrabbed(Runner.LocalPlayer);
                return;
            }

            Runner.RequestStateAuthority(Object.Id);
            Debug.Log($"[Extinguisher] RequestStateAuthority called. object={name}, local={Runner.LocalPlayer}, objectId={Object.Id}, currentStateAuthority={Object.StateAuthority}");
        }

        private void ClearInvalidPendingGrab()
        {
            if (!_pendingGrab)
            {
                return;
            }

            if (!_isLocallySelected ||
                IsHeldByOtherPlayer() ||
                Time.time - _pendingGrabStartedTime >= _grabAuthorityRequestTimeout)
            {
                Debug.Log($"[Extinguisher] Pending grab cleared. object={name}, local={Runner.LocalPlayer}, isLocallySelected={_isLocallySelected}, isHeldByOther={IsHeldByOtherPlayer()}, elapsed={Time.time - _pendingGrabStartedTime:0.000}, timeout={_grabAuthorityRequestTimeout}, isHeld={IsHeld}, heldBy={HeldBy}, stateAuthority={Object.StateAuthority}");
                _pendingGrab = false;
            }
        }

        private void ReleaseIfHeldByLocalPlayer()
        {
            if (!IsNetworkReady || !HasStateAuthority || HeldBy != Runner.LocalPlayer)
            {
                Debug.Log($"[Extinguisher] ReleaseIfHeldByLocalPlayer skipped. object={name}, isNetworkReady={IsNetworkReady}, local={Runner?.LocalPlayer}, hasStateAuthority={HasStateAuthority}, heldBy={HeldBy}, stateAuthority={Object?.StateAuthority}");
                return;
            }

            SetReleased();
        }

        private void SetGrabbed(PlayerRef player)
        {
            if (IsHeld && HeldBy != player)
            {
                Debug.Log($"[Extinguisher] SetGrabbed blocked. object={name}, requestedPlayer={player}, currentHeldBy={HeldBy}, local={Runner.LocalPlayer}, stateAuthority={Object.StateAuthority}");
                return;
            }

            IsHeld = true;
            HeldBy = player;
            Debug.Log($"[Extinguisher] SetGrabbed applied. object={name}, heldBy={HeldBy}, local={Runner.LocalPlayer}, stateAuthority={Object.StateAuthority}, hasStateAuthority={HasStateAuthority}");
        }

        private void SetReleased()
        {
            IsHeld = false;
            HeldBy = PlayerRef.None;
            IsFiring = false;
            EnsureReleasedPhysicsState();
            Debug.Log($"[Extinguisher] SetReleased applied. object={name}, local={Runner.LocalPlayer}, stateAuthority={Object.StateAuthority}, hasStateAuthority={HasStateAuthority}");
        }

        private void EnsureReleasedPhysicsState()
        {
            _rigidbody.isKinematic = false;
            _rigidbody.useGravity = true;
            _rigidbody.WakeUp();
        }

        private void SetFiring(bool firing)
        {
            if (!IsHeldByLocalPlayer || !HasStateAuthority)
            {
                return;
            }

            IsFiring = firing && IsSafetyPinPulled;
        }

        private void TryExtinguishFire()
        {
            Transform rayOrigin = GetRayOrigin();

            if (!Physics.Raycast(
                    rayOrigin.position,
                    rayOrigin.forward,
                    out RaycastHit hit,
                    _range,
                    _fireLayer,
                    QueryTriggerInteraction.Collide))
            {
                return;
            }

            FireObject fire = hit.collider.GetComponentInParent<FireObject>();
            if (fire != null)
            {
                fire.TakeExtinguish(Runner.DeltaTime);
            }
        }

        private void RecoverAbandonedHold()
        {
            if (!Runner.IsSharedModeMasterClient || !IsHeld || IsActivePlayer(HeldBy))
            {
                return;
            }

            SetReleased();
        }

        private bool IsActivePlayer(PlayerRef player)
        {
            foreach (PlayerRef activePlayer in Runner.ActivePlayers)
            {
                if (activePlayer == player)
                {
                    return true;
                }
            }

            return false;
        }

        private Transform GetRayOrigin()
        {
            return _rayOrigin != null ? _rayOrigin : transform;
        }

        private void ApplyNetworkState(bool force)
        {
            if (_safetyPinSocket != null)
            {
                _safetyPinSocket.socketActive = !IsSafetyPinPulled;
            }

            ApplySafetyPinVisuals(IsSafetyPinPulled, force);

            if (!IsHeldByLocalPlayer)
            {
                _grabInteractable.enabled = !IsHeldByOtherPlayer();
            }

            if (!force && _lastRenderedFiring == IsFiring)
            {
                return;
            }

            _lastRenderedFiring = IsFiring;
            ApplyFiringFeedback(IsFiring);
        }

        private void ApplyFiringFeedback(bool firing)
        {
            if (_smokeParticle != null)
            {
                if (firing && !_smokeParticle.isPlaying)
                {
                    _smokeParticle.Play();
                }
                else if (!firing && _smokeParticle.isPlaying)
                {
                    _smokeParticle.Stop();
                }
            }

            if (_extinguisherSfx == null)
            {
                return;
            }

            if (firing && !_extinguisherSfx.isPlaying)
            {
                _extinguisherSfx.Play();
            }
            else if (!firing && _extinguisherSfx.isPlaying)
            {
                _extinguisherSfx.Stop();
            }
        }

        private void ApplySafetyPinVisuals(bool pulled, bool force)
        {
            if (!force && _lastRenderedSafetyPinPulled == pulled)
            {
                return;
            }

            _lastRenderedSafetyPinPulled = pulled;

            foreach (GameObject visual in _safetyPinVisuals)
            {
                if (visual != null)
                {
                    visual.SetActive(!pulled);
                }
            }
        }

        private bool IsHeldByOtherPlayer()
        {
            return IsHeld && HeldBy != Runner.LocalPlayer;
        }
    }
}
