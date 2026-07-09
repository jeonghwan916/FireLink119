using FireLink119.Fire;
using FireLink119.Player;
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

        [Header("Safety Pin Instruction")]
        [SerializeField] private AudioClip _safetyPinInstructionClip;
        [SerializeField] private float _safetyPinInstructionVolume = 1f;

        [Header("Safety Pin Pulled Instruction")]
        [SerializeField] private AudioClip _safetyPinPulledInstructionClip;
        [SerializeField] private float _safetyPinPulledInstructionVolume = 1f;

        [Header("Authority")]
        [SerializeField] private float _grabAuthorityRequestTimeout = 0.75f;

        [Header("Debug")]
        [SerializeField] private bool _logDebug;

        [Networked] private NetworkBool IsHeld { get; set; }
        [Networked] private PlayerRef HeldBy { get; set; }
        [Networked] private NetworkBool IsSafetyPinPulled { get; set; }
        [Networked] private NetworkBool IsFiring { get; set; }
        [Networked] private int GrabInstructionEventId { get; set; }
        [Networked] private int SafetyPinPulledInstructionEventId { get; set; }

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
        private int _lastHandledGrabInstructionEventId;
        private int _lastHandledSafetyPinPulledInstructionEventId;

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

            if (HasStateAuthority)
            {
                IsHeld = false;
                HeldBy = PlayerRef.None;
                IsSafetyPinPulled = false;
                IsFiring = false;
                GrabInstructionEventId = 0;
                SafetyPinPulledInstructionEventId = 0;
                EnsureReleasedPhysicsState();
            }

            ApplyNetworkState(force: true);
            _lastHandledGrabInstructionEventId = GrabInstructionEventId;
            _lastHandledSafetyPinPulledInstructionEventId = SafetyPinPulledInstructionEventId;
            LogDebug($"Spawned. local={Runner.LocalPlayer}, stateAuthority={Object.StateAuthority}, hasStateAuthority={HasStateAuthority}, isMasterClient={Runner.IsSharedModeMasterClient}");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            _pendingGrab = false;
            _isLocallySelected = false;
        }

        public void StateAuthorityChanged()
        {
            LogDebug($"StateAuthorityChanged. local={Runner.LocalPlayer}, stateAuthority={Object.StateAuthority}, hasStateAuthority={HasStateAuthority}, pendingGrab={_pendingGrab}, isHeld={IsHeld}, heldBy={HeldBy}");

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

            if (HasStateAuthority && IsFiring)
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
            ApplyInstructionEvents();
        }

        public void RequestPullSafetyPin()
        {
            TryPullSafetyPin();
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            _isLocallySelected = true;
            RequestGrabAuthority();
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            _isLocallySelected = false;
            _pendingGrab = false;
            ReleaseIfHeldByLocalPlayer();
        }

        private void OnSafetyPinSocketExited(SelectExitEventArgs args)
        {
            TryPullSafetyPin();
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
            if (!IsNetworkReady)
            {
                LogDebug("RequestGrabAuthority blocked. network is not ready.");
                return;
            }

            if (IsHeldByOtherPlayer())
            {
                LogDebug($"RequestGrabAuthority blocked. local={Runner.LocalPlayer}, hasStateAuthority={HasStateAuthority}, stateAuthority={Object.StateAuthority}, isHeld={IsHeld}, heldBy={HeldBy}");
                return;
            }

            _pendingGrab = true;
            _pendingGrabStartedTime = Time.time;

            if (HasStateAuthority)
            {
                _pendingGrab = false;
                SetGrabbed(Runner.LocalPlayer);
                return;
            }

            Runner.RequestStateAuthority(Object.Id);
            LogDebug($"RequestStateAuthority called. local={Runner.LocalPlayer}, objectId={Object.Id}, currentStateAuthority={Object.StateAuthority}");
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
                LogDebug($"Pending grab cleared. local={Runner.LocalPlayer}, isLocallySelected={_isLocallySelected}, isHeldByOther={IsHeldByOtherPlayer()}, isHeld={IsHeld}, heldBy={HeldBy}, stateAuthority={Object.StateAuthority}");
                _pendingGrab = false;
            }
        }

        private void ReleaseIfHeldByLocalPlayer()
        {
            if (!IsNetworkReady)
            {
                LogDebug("Release skipped. network is not ready.");
                return;
            }

            if (!HasStateAuthority || HeldBy != Runner.LocalPlayer)
            {
                LogDebug($"Release skipped. local={Runner.LocalPlayer}, hasStateAuthority={HasStateAuthority}, heldBy={HeldBy}, stateAuthority={Object.StateAuthority}");
                return;
            }

            SetReleased();
        }

        private void SetGrabbed(PlayerRef player)
        {
            if (IsHeld && HeldBy != player)
            {
                LogDebug($"SetGrabbed blocked. requestedPlayer={player}, currentHeldBy={HeldBy}, local={Runner.LocalPlayer}, stateAuthority={Object.StateAuthority}");
                return;
            }

            IsHeld = true;
            HeldBy = player;

            if (!IsSafetyPinPulled && GrabInstructionEventId == 0)
            {
                GrabInstructionEventId++;
            }
        }

        private void SetReleased()
        {
            IsHeld = false;
            HeldBy = PlayerRef.None;
            IsFiring = false;
            EnsureReleasedPhysicsState();
        }

        private void EnsureReleasedPhysicsState()
        {
            _rigidbody.isKinematic = false;
            _rigidbody.useGravity = true;
            _rigidbody.WakeUp();
        }

        private void TryPullSafetyPin()
        {
            if (!IsNetworkReady)
            {
                LogDebug("TryPullSafetyPin skipped. network is not ready.");
                return;
            }

            LogDebug($"TryPullSafetyPin. local={Runner.LocalPlayer}, hasStateAuthority={HasStateAuthority}, stateAuthority={Object.StateAuthority}, isHeld={IsHeld}, heldBy={HeldBy}, isHeldByLocal={IsHeldByLocalPlayer}, isSafetyPinPulled={IsSafetyPinPulled}");

            if (!IsHeldByLocalPlayer || !HasStateAuthority)
            {
                return;
            }

            if (IsSafetyPinPulled)
            {
                return;
            }

            IsSafetyPinPulled = true;

            if (SafetyPinPulledInstructionEventId == 0)
            {
                SafetyPinPulledInstructionEventId++;
            }
        }

        private void SetFiring(bool firing)
        {
            if (!IsNetworkReady)
            {
                LogDebug($"SetFiring skipped. requested={firing}, network is not ready.");
                return;
            }

            if (!IsHeldByLocalPlayer || !HasStateAuthority)
            {
                LogDebug($"SetFiring blocked. requested={firing}, local={Runner.LocalPlayer}, hasStateAuthority={HasStateAuthority}, isHeldByLocal={IsHeldByLocalPlayer}, isSafetyPinPulled={IsSafetyPinPulled}, currentIsFiring={IsFiring}");
                return;
            }

            IsFiring = firing && IsSafetyPinPulled;

            LogDebug($"SetFiring applied. requested={firing}, final={IsFiring}, local={Runner.LocalPlayer}, isSafetyPinPulled={IsSafetyPinPulled}");
        }

        private void TryExtinguishFire()
        {
            Transform rayOrigin = GetRayOrigin();
            Vector3 rayOriginPosition = rayOrigin.position;
            Vector3 rayDirection = rayOrigin.forward;

            if (!Physics.Raycast(
                    rayOriginPosition,
                    rayDirection,
                    out RaycastHit hit,
                    _range,
                    _fireLayer,
                    QueryTriggerInteraction.Collide))
            {
                LogDebug($"TryExtinguishFire miss. isMasterClient={Runner.IsSharedModeMasterClient}, origin={rayOriginPosition}, forward={rayDirection}");
                return;
            }

            FireObject fire = hit.collider.GetComponentInParent<FireObject>();
            LogDebug($"TryExtinguishFire hit. isMasterClient={Runner.IsSharedModeMasterClient}, collider={hit.collider.name}, fire={fire?.name}");

            if (fire != null)
            {
                fire.RequestExtinguish(Runner.DeltaTime);
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

            if (_grabInteractable != null && !IsHeldByLocalPlayer)
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

        private void ApplyInstructionEvents()
        {
            ApplyGrabInstructionEvent();
            ApplySafetyPinPulledInstructionEvent();
        }

        private void ApplyGrabInstructionEvent()
        {
            if (GrabInstructionEventId == _lastHandledGrabInstructionEventId)
            {
                return;
            }

            _lastHandledGrabInstructionEventId = GrabInstructionEventId;

            if (_safetyPinInstructionClip == null)
            {
                return;
            }

            LocalNarrationAudio.PlayOneShot(_safetyPinInstructionClip, _safetyPinInstructionVolume);
        }

        private void ApplySafetyPinPulledInstructionEvent()
        {
            if (SafetyPinPulledInstructionEventId == _lastHandledSafetyPinPulledInstructionEventId)
            {
                return;
            }

            _lastHandledSafetyPinPulledInstructionEventId = SafetyPinPulledInstructionEventId;

            if (_safetyPinPulledInstructionClip == null)
            {
                return;
            }

            LocalNarrationAudio.PlayOneShot(_safetyPinPulledInstructionClip, _safetyPinPulledInstructionVolume);
        }

        private bool IsHeldByOtherPlayer()
        {
            return IsHeld && HeldBy != Runner.LocalPlayer;
        }

        private void LogDebug(string message)
        {
            if (_logDebug)
            {
                Debug.Log($"[Extinguisher] {message}", this);
            }
        }
    }
}
