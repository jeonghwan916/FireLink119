using FireLink119.Fire;
using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FireLink119.Extinguisher
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(XRGrabInteractable))]
    public class Extinguisher : NetworkBehaviour
    {
        [Header("Particle")]
        [SerializeField] private ParticleSystem _smokeParticle;

        [Header("Raycast")]
        [SerializeField] private Transform _rayOrigin;
        [SerializeField] private float _range = 5f;
        [SerializeField] private LayerMask _fireLayer;

        [Header("Safety Pin")]
        [SerializeField] private XRSocketInteractor _safetyPinSocket;
        [SerializeField] private XRGrabInteractable _safetyPinGrabInteractable;
        [SerializeField] private GameObject[] _safetyPinVisuals;

        [Header("Network Pose")]
        [SerializeField] private float _poseSendInterval = 0.03f;

        [Networked] private NetworkBool IsHeld { get; set; }
        [Networked] private PlayerRef HeldBy { get; set; }
        [Networked] private NetworkBool IsSafetyPinPulled { get; set; }
        [Networked] private NetworkBool IsFiring { get; set; }
        [Networked] private Vector3 NetworkedPosition { get; set; }
        [Networked] private Quaternion NetworkedRotation { get; set; }
        [Networked] private Vector3 NetworkedRayOriginPosition { get; set; }
        [Networked] private Quaternion NetworkedRayOriginRotation { get; set; }

        public bool IsNetworkReady => _isSpawned && Object != null && Runner != null;
        public bool NetworkIsHeld => IsNetworkReady && IsHeld;
        public bool NetworkIsSafetyPinPulled => IsNetworkReady && IsSafetyPinPulled;
        public bool NetworkIsFiring => IsNetworkReady && IsFiring;
        public bool IsHeldByLocalPlayer => IsNetworkReady && IsHeld && HeldBy == Runner.LocalPlayer;

        private XRGrabInteractable _grabInteractable;
        private AudioSource _extinguisherSFX;
        private float _nextPoseSendTime;
        private float _nextPoseDebugTime;
        private float _nextPoseReceiveDebugTime;
        private bool _isSpawned;
        private bool _isLocallySelected;
        private bool _lastRenderedFiring;
        private bool _lastRenderedSafetyPinPulled;

        private void Awake()
        {
            _grabInteractable = GetComponent<XRGrabInteractable>();
            _extinguisherSFX = GetComponent<AudioSource>();

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
                NetworkedPosition = transform.position;
                NetworkedRotation = transform.rotation;

                Transform rayOrigin = GetRayOrigin();
                NetworkedRayOriginPosition = rayOrigin.position;
                NetworkedRayOriginRotation = rayOrigin.rotation;
            }

            ApplyNetworkState(force: true);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
        }

        private void Update()
        {
            if (!IsNetworkReady || HasStateAuthority || !ShouldSendLocalPose() || Time.time < _nextPoseSendTime)
            {
                return;
            }

            SendHeldPose();
        }

        public override void FixedUpdateNetwork()
        {
            if (!IsNetworkReady)
            {
                return;
            }

            if (HasStateAuthority)
            {
                if (!IsHeld || HeldBy == Runner.LocalPlayer)
                {
                    NetworkedPosition = transform.position;
                    NetworkedRotation = transform.rotation;

                    Transform rayOrigin = GetRayOrigin();
                    NetworkedRayOriginPosition = rayOrigin.position;
                    NetworkedRayOriginRotation = rayOrigin.rotation;
                }

                if (IsFiring)
                {
                    TryExtinguishFire();
                }
            }

            if (IsLocallyHeld() && Time.time >= _nextPoseSendTime)
            {
                SendHeldPose();
            }
        }

        public override void Render()
        {
            if (!IsNetworkReady)
            {
                return;
            }

            ApplyNetworkState(force: false);

            if (!IsLocallyHeld())
            {
                transform.SetPositionAndRotation(NetworkedPosition, NetworkedRotation);
            }
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            _isLocallySelected = true;
            Debug.Log($"[Extinguisher][OnGrabbed] local={GetLocalPlayerDebug()} hasState={HasStateAuthority} ready={IsNetworkReady} held={NetworkIsHeld} heldByLocal={IsHeldByLocalPlayer} pos={transform.position}");
            RequestGrab();
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            _isLocallySelected = false;
            Debug.Log($"[Extinguisher][OnReleased] local={GetLocalPlayerDebug()} hasState={HasStateAuthority} ready={IsNetworkReady} held={NetworkIsHeld} heldByLocal={IsHeldByLocalPlayer} pos={transform.position}");
            RequestRelease();
        }

        private void OnSafetyPinSocketExited(SelectExitEventArgs args)
        {
            if (!IsHeldByLocalPlayer)
            {
                Debug.Log($"[Extinguisher][SafetyPinSocketExited] ignored because extinguisher is not held by local player. local={GetLocalPlayerDebug()} held={NetworkIsHeld} heldByLocal={IsHeldByLocalPlayer}");
                return;
            }

            RequestSafetyPinPulled();
        }

        private void OnFireStart(ActivateEventArgs args)
        {
            Debug.Log($"[Extinguisher] FireStart local={Runner.LocalPlayer} held={NetworkIsHeld} heldByLocal={IsHeldByLocalPlayer} pin={NetworkIsSafetyPinPulled}");
            RequestFiring(true);
        }

        private void OnFireEnd(DeactivateEventArgs args)
        {
            RequestFiring(false);
        }

        private void RequestGrab()
        {
            if (!IsNetworkReady)
            {
                Debug.LogWarning("[Extinguisher][RequestGrab] ignored because network is not ready.");
                return;
            }

            Debug.Log($"[Extinguisher][RequestGrab] local={Runner.LocalPlayer} hasState={HasStateAuthority} held={IsHeld} heldBy={HeldBy}");

            if (HasStateAuthority)
            {
                SetGrabbed(Runner.LocalPlayer);
                return;
            }

            RPC_RequestGrab();
        }

        private void RequestRelease()
        {
            if (!IsNetworkReady)
            {
                Debug.LogWarning("[Extinguisher][RequestRelease] ignored because network is not ready.");
                return;
            }

            Debug.Log($"[Extinguisher][RequestRelease] local={Runner.LocalPlayer} hasState={HasStateAuthority} held={IsHeld} heldBy={HeldBy}");

            if (HasStateAuthority)
            {
                SetReleased(Runner.LocalPlayer);
                return;
            }

            RPC_RequestRelease();
        }

        private void RequestSafetyPinPulled()
        {
            if (!IsNetworkReady)
            {
                return;
            }

            if (HasStateAuthority)
            {
                SetSafetyPinPulled(Runner.LocalPlayer);
                return;
            }

            RPC_RequestSafetyPinPulled();
        }

        private void RequestFiring(bool firing)
        {
            if (!IsNetworkReady)
            {
                return;
            }

            if (HasStateAuthority)
            {
                SetFiring(Runner.LocalPlayer, firing);
                return;
            }

            RPC_RequestFiring(firing);
        }

        private void SendHeldPose()
        {
            _nextPoseSendTime = Time.time + _poseSendInterval;

            Transform rayOrigin = GetRayOrigin();
            if (Time.time >= _nextPoseDebugTime)
            {
                _nextPoseDebugTime = Time.time + 0.5f;
                Debug.Log($"[Extinguisher][SendPose] local={Runner.LocalPlayer} hasState={HasStateAuthority} selected={_isLocallySelected} held={IsHeld} heldBy={HeldBy} heldByLocal={IsHeldByLocalPlayer} pos={transform.position} rayPos={rayOrigin.position}");
            }

            RPC_SendHeldPose(
                transform.position,
                transform.rotation,
                rayOrigin.position,
                rayOrigin.rotation);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestGrab(RpcInfo info = default)
        {
            Debug.Log($"[Extinguisher][RPC_RequestGrab] receiverLocal={Runner.LocalPlayer} source={info.Source} hasState={HasStateAuthority} held={IsHeld} heldBy={HeldBy}");
            SetGrabbed(info.Source);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestRelease(RpcInfo info = default)
        {
            Debug.Log($"[Extinguisher][RPC_RequestRelease] receiverLocal={Runner.LocalPlayer} source={info.Source} hasState={HasStateAuthority} held={IsHeld} heldBy={HeldBy}");
            SetReleased(info.Source);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestSafetyPinPulled(RpcInfo info = default)
        {
            SetSafetyPinPulled(info.Source);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestFiring(NetworkBool firing, RpcInfo info = default)
        {
            SetFiring(info.Source, firing);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, Channel = RpcChannel.Unreliable)]
        private void RPC_SendHeldPose(
            Vector3 position,
            Quaternion rotation,
            Vector3 rayOriginPosition,
            Quaternion rayOriginRotation,
            RpcInfo info = default)
        {
            if (Time.time >= _nextPoseReceiveDebugTime)
            {
                _nextPoseReceiveDebugTime = Time.time + 0.5f;
                Debug.Log($"[Extinguisher][RecvPose] receiverLocal={Runner.LocalPlayer} source={info.Source} held={IsHeld} heldBy={HeldBy} accepted={IsHeld && HeldBy == info.Source} pos={position} rayPos={rayOriginPosition}");
            }

            if (!IsHeld || HeldBy != info.Source)
            {
                return;
            }

            transform.SetPositionAndRotation(position, rotation);
            NetworkedPosition = position;
            NetworkedRotation = rotation;
            NetworkedRayOriginPosition = rayOriginPosition;
            NetworkedRayOriginRotation = rayOriginRotation;
        }

        private void SetGrabbed(PlayerRef player)
        {
            Debug.Log($"[Extinguisher][SetGrabbed] player={player} beforeHeld={IsHeld} beforeHeldBy={HeldBy}");

            if (IsHeld && HeldBy != player)
            {
                Debug.Log($"[Extinguisher][SetGrabbed] rejected player={player} currentHeldBy={HeldBy}");
                return;
            }

            IsHeld = true;
            HeldBy = player;

            Debug.Log($"[Extinguisher][SetGrabbed] afterHeld={IsHeld} afterHeldBy={HeldBy}");
        }

        private void SetReleased(PlayerRef player)
        {
            Debug.Log($"[Extinguisher][SetReleased] player={player} beforeHeld={IsHeld} beforeHeldBy={HeldBy}");

            if (!IsHeld || HeldBy != player)
            {
                Debug.Log($"[Extinguisher][SetReleased] rejected player={player} currentHeld={IsHeld} currentHeldBy={HeldBy}");
                return;
            }

            IsHeld = false;
            HeldBy = PlayerRef.None;
            IsFiring = false;
            NetworkedPosition = transform.position;
            NetworkedRotation = transform.rotation;

            Transform rayOrigin = GetRayOrigin();
            NetworkedRayOriginPosition = rayOrigin.position;
            NetworkedRayOriginRotation = rayOrigin.rotation;

            Debug.Log($"[Extinguisher][SetReleased] afterHeld={IsHeld} afterHeldBy={HeldBy} pos={NetworkedPosition}");
        }

        private void SetSafetyPinPulled(PlayerRef player)
        {
            Debug.Log($"[Extinguisher] SetSafetyPinPulled player={player} held={IsHeld} heldBy={HeldBy}");

            if (!IsHeld || HeldBy != player)
            {
                return;
            }

            IsSafetyPinPulled = true;
        }

        private void SetFiring(PlayerRef player, bool firing)
        {
            Debug.Log($"[Extinguisher] SetFiring player={player} held={IsHeld} heldBy={HeldBy} pin={IsSafetyPinPulled} firing={firing}");

            if (!IsHeld || HeldBy != player)
            {
                return;
            }

            IsFiring = firing && IsSafetyPinPulled;
        }

        private void TryExtinguishFire()
        {
            Vector3 origin = NetworkedRayOriginPosition;
            Vector3 direction = NetworkedRayOriginRotation * Vector3.forward;

            if (!Physics.Raycast(
                    origin,
                    direction,
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

            if (_grabInteractable != null && !IsLocallyHeld())
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

            if (_extinguisherSFX == null)
            {
                return;
            }

            if (firing && !_extinguisherSFX.isPlaying)
            {
                _extinguisherSFX.Play();
            }
            else if (!firing && _extinguisherSFX.isPlaying)
            {
                _extinguisherSFX.Stop();
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

        private bool IsLocallyHeld()
        {
            return IsHeld && HeldBy == Runner.LocalPlayer;
        }

        private bool IsHeldByOtherPlayer()
        {
            return IsHeld && HeldBy != Runner.LocalPlayer;
        }

        private bool ShouldSendLocalPose()
        {
            return _isLocallySelected || IsLocallyHeld();
        }

        private string GetLocalPlayerDebug()
        {
            return Runner != null ? Runner.LocalPlayer.ToString() : "NoRunner";
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
}
