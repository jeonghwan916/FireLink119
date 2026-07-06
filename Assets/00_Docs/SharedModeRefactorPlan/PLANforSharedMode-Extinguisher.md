# PLAN for Shared Mode - Extinguisher.cs

```csharp
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
        private AudioSource _extinguisherSfx;
        private bool _isSpawned;
        private bool _isLocallySelected;
        private bool _pendingGrab;
        private bool _lastRenderedFiring;
        private bool _lastRenderedSafetyPinPulled;

        private void Awake()
        {
            _grabInteractable = GetComponent<XRGrabInteractable>();
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
                WriteCurrentPose();
            }

            ApplyNetworkState(force: true);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            _pendingGrab = false;
            _isLocallySelected = false;
        }

        public void StateAuthorityChanged()
        {
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

            if (HasStateAuthority)
            {
                RecoverAbandonedHold();

                if (!IsHeld || HeldBy == Runner.LocalPlayer)
                {
                    WriteCurrentPose();
                }
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

            if (!IsHeldByLocalPlayer)
            {
                transform.SetPositionAndRotation(NetworkedPosition, NetworkedRotation);
            }
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
                return;
            }

            _pendingGrab = true;

            if (HasStateAuthority)
            {
                _pendingGrab = false;
                SetGrabbed(Runner.LocalPlayer);
                return;
            }

            Runner.RequestStateAuthority(Object.Id);
        }

        private void ReleaseIfHeldByLocalPlayer()
        {
            if (!IsNetworkReady || !HasStateAuthority || HeldBy != Runner.LocalPlayer)
            {
                return;
            }

            SetReleased();
            Runner.ReleaseStateAuthority(Object.Id);
        }

        private void SetGrabbed(PlayerRef player)
        {
            if (IsHeld && HeldBy != player)
            {
                return;
            }

            IsHeld = true;
            HeldBy = player;
            WriteCurrentPose();
        }

        private void SetReleased()
        {
            IsHeld = false;
            HeldBy = PlayerRef.None;
            IsFiring = false;
            WriteCurrentPose();
        }

        private void SetFiring(bool firing)
        {
            if (!IsHeldByLocalPlayer || !HasStateAuthority)
            {
                return;
            }

            IsFiring = firing && IsSafetyPinPulled;
        }

        private void WriteCurrentPose()
        {
            Transform rayOrigin = GetRayOrigin();

            NetworkedPosition = transform.position;
            NetworkedRotation = transform.rotation;
            NetworkedRayOriginPosition = rayOrigin.position;
            NetworkedRayOriginRotation = rayOrigin.rotation;
        }

        private void TryExtinguishFire()
        {
            Vector3 direction = NetworkedRayOriginRotation * Vector3.forward;

            if (!Physics.Raycast(
                    NetworkedRayOriginPosition,
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
```

## Self Review

- 잡기 시작 시 `RpcSources.All -> StateAuthority` 요청을 쓰지 않고 `Runner.RequestStateAuthority(Object.Id)`만 사용한다. Shared Mode에서 소화기 권위는 실제 잡은 플레이어가 가져야 하기 때문이다.
- `StateAuthorityChanged()`에서 잡기 확정을 처리하므로 권위 요청 성공 전 상태를 먼저 쓰지 않는다.
- pose, ray origin, 안전핀, 분사 상태는 `HasStateAuthority`인 잡은 플레이어만 `[Networked]` 값으로 기록한다.
- 화재 진화 판정은 `Runner.IsSharedModeMasterClient` 피어만 수행한다. 잡은 플레이어가 Master Client가 아니어도 Master Client가 복제된 ray pose와 `IsFiring`을 읽고 `FireObject.TakeExtinguish()`를 호출한다.
- `FireObject.TakeExtinguish()`는 화재 객체의 `StateAuthority`에서만 성공하므로, 화재 객체가 `Is Master Client`로 설정되어 있어야 한다.
- 놓을 때 `SetReleased()` 후 `Runner.ReleaseStateAuthority(Object.Id)`를 호출해 계획의 Master Client 회수 방향과 맞춘다.
- 플레이어 이탈로 잡힌 상태가 남는 경우를 줄이기 위해 Master Client가 권위를 가진 상태에서 `HeldBy`가 active player가 아니면 release한다.
- 디버그 로그, `InputAuthority`, `runner.IsServer`, Host/Client 역할 분기, 불필요한 pose RPC는 제거했다.
