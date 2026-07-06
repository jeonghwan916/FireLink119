# PLAN for Shared Mode - NPCController.cs

## Assumptions

- `NPCController`가 붙은 `NetworkObject`는 Shared Mode에서 `Is Master Client`로 설정한다.
- NPC는 월드 규칙 객체이므로 `Allow State Authority Override`를 켜지 않는다.
- NPC 이동, 사망, 문 열기, 목적지 전환, 대사 이벤트 번호 증가는 `StateAuthority`, 즉 Master Client만 확정한다.
- 플레이어가 직접 요청할 수 있는 동작은 `RequestFollowPlayer(PlayerRef)`뿐이며, RPC 송신자와 요청 대상 `PlayerRef`가 같을 때만 허용한다.
- 목적지/사망/대사/웅크리기 요청은 `NPCDestinationSettingTrigger`, `NPCDeadTrigger`, 애니메이션 이벤트처럼 Master Client 권위 객체 또는 NPC 권위 객체에서 직접 호출하는 경로만 유효하다.
- `Runner.GetPlayerObject(player)`가 Shared Mode 아바타 스폰 구조에서 모든 피어에 안정적으로 세팅된다는 전제가 필요하다. 이 전제가 깨지면 NPC 추적은 대상 해석에 실패한다.

## Redesigned NPCController.cs

```csharp
using Fusion;
using FireLink119.Network;
using UnityEngine;
using UnityEngine.AI;

namespace FireLink119.NPC
{
    public enum NPCDeathType
    {
        None = 0,
        Explosion = 1,
        Smoke = 2
    }

    public enum NPCTargetMode
    {
        None = 0,
        FollowPlayer = 1,
        Destination = 2,
        DoorThenDestination = 3
    }

    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Animator))]
    public class NPCController : NetworkBehaviour
    {
        private const int InvalidTargetId = -1;

        [Header("Network Targets")]
        [SerializeField] private Transform[] _destinationTargets;
        [SerializeField] private Transform[] _doorTargets;
        [SerializeField] private NetworkDoor[] _networkDoors;

        [Header("Movement")]
        [SerializeField] private float _walkSpeed = 2f;
        [SerializeField] private float _runSpeed = 6f;
        [SerializeField] private float _crouchWalkSpeed = 2f;
        [SerializeField] private float _normalStopDistance = 2.5f;
        [SerializeField] private float _doorStopDistance = 1.25f;
        [SerializeField] private float _runDistance = 6f;
        [SerializeField] private float _repathInterval = 0.1f;
        [SerializeField] private float _currentTargetMoveThreshold = 0.25f;
        [SerializeField] private float _currentTargetSampleRadius = 2f;

        [Header("Animation")]
        [SerializeField] private float _animationDampTime = 0.12f;
        [SerializeField] private float _openDoorCancelTransitionDuration = 0.05f;

        [Header("Initial State")]
        [SerializeField] private NPCState _initialState = NPCState.Idle;
        [SerializeField] private bool _initialIsCrouching;

        [Header("Calmdown Dialogue")]
        [SerializeField] private AudioClip[] _calmDownClips;
        [SerializeField] private string[] _calmDownTexts;

        [Header("Network Dialogue")]
        [SerializeField] private AudioClip[] _dialogueClips;
        [SerializeField] private string[] _dialogueTexts;

        [Networked] public NPCState State { get; private set; }
        [Networked] public NetworkBool IsDead { get; private set; }
        [Networked] public NetworkBool IsOpeningDoor { get; private set; }
        [Networked] public NetworkBool IsCrouching { get; private set; }
        [Networked] private Vector3 NetworkPosition { get; set; }
        [Networked] private Quaternion NetworkRotation { get; set; }
        [Networked] private float NetworkMoveSpeed { get; set; }
        [Networked] private NPCTargetMode TargetMode { get; set; }
        [Networked] private PlayerRef FollowPlayer { get; set; }
        [Networked] private int CurrentDoorId { get; set; }
        [Networked] private int CurrentDestinationId { get; set; }
        [Networked] private int DialogueEventId { get; set; }
        [Networked] private int DialogueClipIndex { get; set; }
        [Networked] private NetworkBool IsCalmDownDialogue { get; set; }
        [Networked] private NPCDeathType DeathType { get; set; }
        [Networked] private int DeathEventId { get; set; }

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int MotionSpeedHash = Animator.StringToHash("MotionSpeed");
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");
        private static readonly int IsCrouchingHash = Animator.StringToHash("IsCrouching");
        private static readonly int OpenDoorHash = Animator.StringToHash("OpenDoor");
        private static readonly int DeathByExplosionHash = Animator.StringToHash("DeathByExplosion");
        private static readonly int DeathBySmokeHash = Animator.StringToHash("DeathBySmoke");
        private static readonly int IdleWalkRunBlendHash = Animator.StringToHash("Base Layer.Idle Walk Run Blend");

        private NavMeshAgent _agent;
        private Animator _animator;
        private AudioSource _audioSource;

        private Transform _currentTarget;
        private Vector3 _lastDestination = Vector3.positiveInfinity;
        private float _nextRepathTime;
        private int _lastHandledDialogueEventId;
        private int _lastHandledDeathEventId;
        private bool _hasPlayedOpeningDoorTrigger;
        private bool _hasConfiguredAgent;
        private bool _agentConfiguredAsAuthority;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _animator = GetComponent<Animator>();
            _audioSource = GetComponent<AudioSource>();
        }

        public override void Spawned()
        {
            if (HasStateAuthority)
            {
                State = _initialState;
                IsDead = false;
                IsOpeningDoor = false;
                IsCrouching = _initialIsCrouching;
                NetworkPosition = transform.position;
                NetworkRotation = transform.rotation;
                NetworkMoveSpeed = 0f;
                TargetMode = NPCTargetMode.None;
                FollowPlayer = default;
                CurrentDoorId = InvalidTargetId;
                CurrentDestinationId = InvalidTargetId;
                DialogueEventId = 0;
                DialogueClipIndex = InvalidTargetId;
                IsCalmDownDialogue = false;
                DeathType = NPCDeathType.None;
                DeathEventId = 0;
            }

            ConfigureAgentForAuthority();
            RefreshAuthorityTargetFromNetworkState();
            ApplyNetworkPose();
            ApplyNetworkAnimation();
        }

        public override void FixedUpdateNetwork()
        {
            ConfigureAgentForAuthority();

            if (!HasStateAuthority || IsDead)
            {
                return;
            }

            RefreshAuthorityTargetFromNetworkState();
            UpdateAuthorityFollowTarget();
            UpdateAuthorityState();
            UpdateAuthorityDestination();
            UpdateAuthorityMoveSpeed();
            WriteNetworkPose();
        }

        public override void Render()
        {
            ConfigureAgentForAuthority();
            ApplyNetworkPose();
            ApplyNetworkAnimation();
            ApplyNetworkEvents();
        }

        public void RequestFollowPlayer(PlayerRef player)
        {
            if (HasStateAuthority)
            {
                if (CanRequesterControlPlayer(player, Runner != null ? Runner.LocalPlayer : default))
                {
                    ApplyFollowPlayer(player);
                }

                return;
            }

            RPC_RequestFollowPlayer(player);
        }

        public void RequestSetDestination(int destinationId)
        {
            if (HasStateAuthority)
            {
                ApplyDestination(destinationId);
                return;
            }

            RPC_RequestSetDestination(destinationId);
        }

        public void RequestSetDestinationViaDoor(int doorId, int destinationId)
        {
            if (HasStateAuthority)
            {
                ApplyDestinationViaDoor(doorId, destinationId);
                return;
            }

            RPC_RequestSetDestinationViaDoor(doorId, destinationId);
        }

        public void RequestToggleCrouch()
        {
            if (HasStateAuthority)
            {
                ApplyToggleCrouch();
                return;
            }

            RPC_RequestToggleCrouch();
        }

        public void RequestDie(NPCDeathType deathType)
        {
            if (HasStateAuthority)
            {
                ApplyDeath(deathType);
                return;
            }

            RPC_RequestDie(deathType);
        }

        public void RequestPlayDialogue(int dialogueId)
        {
            if (HasStateAuthority)
            {
                ApplyDialogue(dialogueId);
                return;
            }

            RPC_RequestPlayDialogue(dialogueId);
        }

        public void ToggleCrouch()
        {
            RequestToggleCrouch();
        }

        public void FinishOpeningDoor()
        {
            NotifyOpeningDoorAnimationFinished();
        }

        public void NotifyOpeningDoorAnimationFinished()
        {
            if (HasStateAuthority && IsOpeningDoor)
            {
                CompleteOpeningDoor();
            }
        }

        public void DieByExplosion()
        {
            RequestDie(NPCDeathType.Explosion);
        }

        public void DieBySmoke()
        {
            RequestDie(NPCDeathType.Smoke);
        }

        public void PlayDialogue(AudioClip clip, string text)
        {
            if (_audioSource != null && clip != null)
            {
                _audioSource.PlayOneShot(clip);
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestFollowPlayer(PlayerRef player, RpcInfo info = default)
        {
            if (CanRequesterControlPlayer(player, info.Source))
            {
                ApplyFollowPlayer(player);
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestSetDestination(int destinationId, RpcInfo info = default)
        {
            if (CanAcceptWorldStateRequest(info.Source))
            {
                ApplyDestination(destinationId);
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestSetDestinationViaDoor(int doorId, int destinationId, RpcInfo info = default)
        {
            if (CanAcceptWorldStateRequest(info.Source))
            {
                ApplyDestinationViaDoor(doorId, destinationId);
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestToggleCrouch(RpcInfo info = default)
        {
            if (CanAcceptWorldStateRequest(info.Source))
            {
                ApplyToggleCrouch();
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestDie(NPCDeathType deathType, RpcInfo info = default)
        {
            if (CanAcceptWorldStateRequest(info.Source))
            {
                ApplyDeath(deathType);
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestPlayDialogue(int dialogueId, RpcInfo info = default)
        {
            if (CanAcceptWorldStateRequest(info.Source))
            {
                ApplyDialogue(dialogueId);
            }
        }

        private void ConfigureAgentForAuthority()
        {
            if (_agent == null)
            {
                return;
            }

            if (_hasConfiguredAgent && _agentConfiguredAsAuthority == HasStateAuthority)
            {
                return;
            }

            _hasConfiguredAgent = true;
            _agentConfiguredAsAuthority = HasStateAuthority;

            if (HasStateAuthority)
            {
                _agent.enabled = true;
                _agent.updatePosition = true;
                _agent.updateRotation = true;
                _agent.isStopped = false;
                _agent.Warp(NetworkPosition);
                transform.SetPositionAndRotation(NetworkPosition, NetworkRotation);
                ApplyStopDistanceForState();
                SetAuthorityMoveSpeed(NetworkMoveSpeed);
                SetCurrentTarget(_currentTarget);
                return;
            }

            _agent.updatePosition = false;
            _agent.updateRotation = false;
            _agent.enabled = false;
        }

        private void UpdateAuthorityState()
        {
            if (IsOpeningDoor || !HasArrived())
            {
                return;
            }

            if (State == NPCState.GoingDoor)
            {
                BeginOpeningDoor();
                return;
            }

            if (State == NPCState.GoingFinalDestination)
            {
                CompleteRoute();
            }
        }

        private void UpdateAuthorityDestination()
        {
            if (IsOpeningDoor)
            {
                return;
            }

            if (_currentTarget == null)
            {
                StopMoving();
                return;
            }

            if (GetAuthorityTime() < _nextRepathTime)
            {
                return;
            }

            Vector3 targetPosition = _currentTarget.position;
            float moveThresholdSqr = _currentTargetMoveThreshold * _currentTargetMoveThreshold;

            if ((_lastDestination - targetPosition).sqrMagnitude < moveThresholdSqr)
            {
                return;
            }

            if (NavMesh.SamplePosition(targetPosition, out NavMeshHit hit, _currentTargetSampleRadius, _agent.areaMask))
            {
                _agent.SetDestination(hit.position);
                _lastDestination = hit.position;
            }
            else
            {
                StopMoving();
            }

            _nextRepathTime = GetAuthorityTime() + _repathInterval;
        }

        private void UpdateAuthorityMoveSpeed()
        {
            if (IsOpeningDoor || !IsMovingToTarget())
            {
                SetAuthorityMoveSpeed(0f);
                return;
            }

            float targetSpeed = IsCrouching ? _crouchWalkSpeed : ShouldRun() ? _runSpeed : _walkSpeed;
            SetAuthorityMoveSpeed(targetSpeed);
        }

        private void UpdateAuthorityFollowTarget()
        {
            if (TargetMode != NPCTargetMode.FollowPlayer)
            {
                return;
            }

            Transform target = ResolveFollowTarget(FollowPlayer);
            if (target == null)
            {
                CompleteRoute();
                return;
            }

            if (_currentTarget != target)
            {
                SetCurrentTarget(target);
            }
        }

        private void RefreshAuthorityTargetFromNetworkState()
        {
            if (!HasStateAuthority)
            {
                return;
            }

            switch (TargetMode)
            {
                case NPCTargetMode.FollowPlayer:
                    SetCurrentTargetIfChanged(ResolveFollowTarget(FollowPlayer));
                    break;
                case NPCTargetMode.Destination:
                    SetCurrentTargetIfChanged(ResolveDestinationTarget(CurrentDestinationId));
                    break;
                case NPCTargetMode.DoorThenDestination:
                    SetCurrentTargetIfChanged(State == NPCState.GoingFinalDestination
                        ? ResolveDestinationTarget(CurrentDestinationId)
                        : ResolveDoorTarget(CurrentDoorId));
                    break;
                default:
                    SetCurrentTargetIfChanged(null);
                    break;
            }
        }

        private bool ShouldRun()
        {
            return _agent != null &&
                   !_agent.pathPending &&
                   _agent.remainingDistance > _runDistance;
        }

        private bool IsMovingToTarget()
        {
            return _agent != null &&
                   _agent.enabled &&
                   _agent.hasPath &&
                   !_agent.pathPending &&
                   _agent.remainingDistance > _agent.stoppingDistance;
        }

        private bool HasArrived()
        {
            if (_agent == null || _currentTarget == null || _agent.pathPending)
            {
                return false;
            }

            if (_agent.hasPath)
            {
                return _agent.remainingDistance <= _agent.stoppingDistance;
            }

            return Vector3.Distance(transform.position, _currentTarget.position) <= _agent.stoppingDistance;
        }

        private void SetAuthorityMoveSpeed(float speed)
        {
            NetworkMoveSpeed = speed;

            if (_agent != null && _agent.enabled)
            {
                _agent.speed = speed;
            }
        }

        private void StopMoving()
        {
            if (_agent != null && _agent.enabled && _agent.hasPath)
            {
                _agent.ResetPath();
            }

            _lastDestination = Vector3.positiveInfinity;
            SetAuthorityMoveSpeed(0f);
        }

        private void SetState(NPCState state)
        {
            State = state;
            ApplyStopDistanceForState();
        }

        private void ApplyStopDistanceForState()
        {
            if (_agent == null || !_agent.enabled)
            {
                return;
            }

            _agent.stoppingDistance = State == NPCState.GoingDoor
                ? _doorStopDistance
                : _normalStopDistance;
        }

        private void ApplyFollowPlayer(PlayerRef player)
        {
            Transform target = ResolveFollowTarget(player);
            if (target == null)
            {
                return;
            }

            if (IsRouteInProgress())
            {
                ApplyRandomCalmDownDialogue();
            }

            CancelOpeningDoor();
            FollowPlayer = player;
            CurrentDoorId = InvalidTargetId;
            CurrentDestinationId = InvalidTargetId;
            TargetMode = NPCTargetMode.FollowPlayer;

            SetState(NPCState.Follow);
            SetCurrentTarget(target);
        }

        private void ApplyDestination(int destinationId)
        {
            Transform destination = ResolveDestinationTarget(destinationId);

            CancelOpeningDoor();
            FollowPlayer = default;
            CurrentDoorId = InvalidTargetId;
            CurrentDestinationId = destination == null ? InvalidTargetId : destinationId;
            TargetMode = destination == null ? NPCTargetMode.None : NPCTargetMode.Destination;

            SetState(destination == null ? NPCState.Idle : NPCState.GoingFinalDestination);
            SetCurrentTarget(destination);
        }

        private void ApplyDestinationViaDoor(int doorId, int destinationId)
        {
            Transform doorTarget = ResolveDoorTarget(doorId);
            Transform finalDestination = ResolveDestinationTarget(destinationId);

            if (doorTarget == null)
            {
                ApplyDestination(destinationId);
                return;
            }

            CancelOpeningDoor();
            FollowPlayer = default;
            CurrentDoorId = doorId;
            CurrentDestinationId = finalDestination == null ? InvalidTargetId : destinationId;
            TargetMode = NPCTargetMode.DoorThenDestination;

            SetState(NPCState.GoingDoor);
            SetCurrentTarget(doorTarget);
        }

        private void ApplyToggleCrouch()
        {
            if (!IsDead)
            {
                IsCrouching = !IsCrouching;
            }
        }

        private void BeginOpeningDoor()
        {
            if (IsOpeningDoor)
            {
                return;
            }

            SetState(NPCState.OpeningDoor);
            IsOpeningDoor = true;
            _hasPlayedOpeningDoorTrigger = false;
            StopMoving();
        }

        private void CompleteOpeningDoor()
        {
            IsOpeningDoor = false;
            OpenCurrentDoor();

            Transform destination = ResolveDestinationTarget(CurrentDestinationId);
            if (destination != null)
            {
                SetState(NPCState.GoingFinalDestination);
                SetCurrentTarget(destination);
                return;
            }

            CompleteRoute();
        }

        private void CancelOpeningDoor()
        {
            if (!IsOpeningDoor)
            {
                return;
            }

            IsOpeningDoor = false;
            _hasPlayedOpeningDoorTrigger = false;
            ApplyStopDistanceForState();
        }

        private void CompleteRoute()
        {
            StopMoving();
            FollowPlayer = default;
            CurrentDoorId = InvalidTargetId;
            CurrentDestinationId = InvalidTargetId;
            TargetMode = NPCTargetMode.None;
            SetCurrentTarget(null);
            SetState(NPCState.Idle);
        }

        private void ApplyDeath(NPCDeathType deathType)
        {
            if (IsDead)
            {
                return;
            }

            IsDead = true;
            IsOpeningDoor = false;
            IsCrouching = false;
            DeathType = deathType;
            DeathEventId++;

            SetState(NPCState.Dead);
            FollowPlayer = default;
            CurrentDoorId = InvalidTargetId;
            CurrentDestinationId = InvalidTargetId;
            TargetMode = NPCTargetMode.None;
            SetCurrentTarget(null);
            StopMoving();

            if (_agent != null && _agent.enabled)
            {
                _agent.isStopped = true;
            }

            NetworkMoveSpeed = 0f;
            WriteNetworkPose();
        }

        private void ApplyRandomCalmDownDialogue()
        {
            if (_calmDownClips == null || _calmDownClips.Length == 0)
            {
                return;
            }

            int textCount = _calmDownTexts == null ? 0 : _calmDownTexts.Length;
            int availableCount = textCount > 0 ? Mathf.Min(_calmDownClips.Length, textCount) : _calmDownClips.Length;

            if (availableCount <= 0)
            {
                return;
            }

            DialogueClipIndex = Random.Range(0, availableCount);
            IsCalmDownDialogue = true;
            DialogueEventId++;
        }

        private void ApplyDialogue(int dialogueId)
        {
            if (_dialogueClips == null ||
                dialogueId < 0 ||
                dialogueId >= _dialogueClips.Length)
            {
                return;
            }

            DialogueClipIndex = dialogueId;
            IsCalmDownDialogue = false;
            DialogueEventId++;
        }

        private bool CanRequesterControlPlayer(PlayerRef requestedPlayer, PlayerRef requester)
        {
            return requester != default && requester == requestedPlayer;
        }

        private bool CanAcceptWorldStateRequest(PlayerRef requester)
        {
            return requester == default;
        }

        private bool IsRouteInProgress()
        {
            return State == NPCState.GoingDoor ||
                   State == NPCState.OpeningDoor ||
                   State == NPCState.GoingFinalDestination;
        }

        private void SetCurrentTarget(Transform target)
        {
            _currentTarget = target;
            _lastDestination = Vector3.positiveInfinity;
            _nextRepathTime = 0f;
        }

        private void SetCurrentTargetIfChanged(Transform target)
        {
            if (_currentTarget != target)
            {
                SetCurrentTarget(target);
            }
        }

        private Transform ResolveFollowTarget(PlayerRef player)
        {
            if (Runner == null || player == default)
            {
                return null;
            }

            NetworkObject playerObject = Runner.GetPlayerObject(player);
            return playerObject != null ? playerObject.transform : null;
        }

        private Transform ResolveDestinationTarget(int destinationId)
        {
            if (_destinationTargets == null ||
                destinationId < 0 ||
                destinationId >= _destinationTargets.Length)
            {
                return null;
            }

            return _destinationTargets[destinationId];
        }

        private Transform ResolveDoorTarget(int doorId)
        {
            if (_doorTargets == null ||
                doorId < 0 ||
                doorId >= _doorTargets.Length)
            {
                return null;
            }

            return _doorTargets[doorId];
        }

        private void OpenCurrentDoor()
        {
            NetworkDoor door = ResolveNetworkDoor(CurrentDoorId);
            if (door != null)
            {
                door.Open();
            }
        }

        private NetworkDoor ResolveNetworkDoor(int doorId)
        {
            if (_networkDoors == null || doorId == InvalidTargetId)
            {
                return null;
            }

            for (int i = 0; i < _networkDoors.Length; i++)
            {
                NetworkDoor door = _networkDoors[i];
                if (door != null && door.DoorId == doorId)
                {
                    return door;
                }
            }

            return null;
        }

        private float GetAuthorityTime()
        {
            return Runner == null ? Time.time : (float)Runner.SimulationTime;
        }

        private void WriteNetworkPose()
        {
            NetworkPosition = transform.position;
            NetworkRotation = transform.rotation;
        }

        private void ApplyNetworkPose()
        {
            if (!HasStateAuthority)
            {
                transform.SetPositionAndRotation(NetworkPosition, NetworkRotation);
            }
        }

        private void ApplyNetworkAnimation()
        {
            if (_animator == null)
            {
                return;
            }

            float speed = IsDead ? 0f : NetworkMoveSpeed;

            _animator.SetFloat(SpeedHash, speed, _animationDampTime, Time.deltaTime);
            _animator.SetFloat(MotionSpeedHash, IsDead ? 0f : 1f, _animationDampTime, Time.deltaTime);
            _animator.SetBool(GroundedHash, true);
            _animator.SetBool(IsCrouchingHash, IsCrouching);

            if (IsOpeningDoor && !_hasPlayedOpeningDoorTrigger)
            {
                _animator.ResetTrigger(OpenDoorHash);
                _animator.SetTrigger(OpenDoorHash);
                _hasPlayedOpeningDoorTrigger = true;
            }

            if (!IsOpeningDoor && _hasPlayedOpeningDoorTrigger && State != NPCState.OpeningDoor)
            {
                _animator.ResetTrigger(OpenDoorHash);
                _animator.CrossFade(IdleWalkRunBlendHash, _openDoorCancelTransitionDuration);
                _hasPlayedOpeningDoorTrigger = false;
            }
        }

        private void ApplyNetworkEvents()
        {
            ApplyDeathEvent();
            ApplyDialogueEvent();
        }

        private void ApplyDeathEvent()
        {
            if (DeathEventId == _lastHandledDeathEventId)
            {
                return;
            }

            _lastHandledDeathEventId = DeathEventId;

            if (_animator == null || DeathType == NPCDeathType.None)
            {
                return;
            }

            _animator.ResetTrigger(OpenDoorHash);
            _animator.SetFloat(SpeedHash, 0f);
            _animator.SetFloat(MotionSpeedHash, 0f);
            _animator.SetBool(IsCrouchingHash, false);

            switch (DeathType)
            {
                case NPCDeathType.Explosion:
                    _animator.SetTrigger(DeathByExplosionHash);
                    break;
                case NPCDeathType.Smoke:
                    _animator.SetTrigger(DeathBySmokeHash);
                    break;
            }
        }

        private void ApplyDialogueEvent()
        {
            if (DialogueEventId == _lastHandledDialogueEventId)
            {
                return;
            }

            _lastHandledDialogueEventId = DialogueEventId;

            AudioClip[] clips = IsCalmDownDialogue ? _calmDownClips : _dialogueClips;
            string[] texts = IsCalmDownDialogue ? _calmDownTexts : _dialogueTexts;

            if (clips == null || DialogueClipIndex < 0 || DialogueClipIndex >= clips.Length)
            {
                return;
            }

            AudioClip clip = clips[DialogueClipIndex];
            string text = texts != null && DialogueClipIndex < texts.Length
                ? texts[DialogueClipIndex]
                : string.Empty;

            PlayDialogue(clip, text);
        }
    }
}
```

## Self Review

- `NavMeshAgent`는 `HasStateAuthority`일 때만 활성화된다. `ConfigureAgentForAuthority()`를 `FixedUpdateNetwork()`와 `Render()`에서 반복 확인해 Master Client 변경 후에도 새 권위자가 에이전트를 켤 수 있게 했다.
- 권위 이전 시 `_currentTarget` 같은 로컬 참조가 사라지는 문제를 막기 위해 `TargetMode`, `FollowPlayer`, `CurrentDoorId`, `CurrentDestinationId`를 `[Networked]`로 유지하고 권위자가 매 tick 대상 Transform을 재해석한다.
- 플레이어 추적 RPC는 `info.Source == requestedPlayer`일 때만 통과한다. 다른 클라이언트가 남의 `PlayerRef`로 NPC를 끌고 가는 경로를 막는다.
- 목적지, 문 경유, 웅크리기, 사망, 대사 RPC는 원격 플레이어 요청을 받지 않는다. 현재 설계에서는 Master Client 권위 트리거가 직접 호출해야 한다.
- `InputAuthority`, `HasInputAuthority`, `runner.IsServer`, `PlayerType` 기반 런타임 로직을 사용하지 않는다.
- `NetworkDoor.Open()`은 문도 Master Client 권위 객체라는 전제에서만 성공한다. 문 권위 설정이 다르면 NPC 문 열기가 no-op이므로 `NetworkDoor` prefab/scene 설정 검증이 필요하다.
- 디버그 로그, legacy Transform 경고, 과도한 방어 코드는 제거했다. 단, 현재 `NPCDestinationSettingTrigger`의 legacy `_doorTarget`/`_destination` 경로는 후속 리팩토링에서 ID 기반 요청만 남겨야 이 코드와 완전히 맞는다.
- 단독으로 실제 `NPCController.cs`에 적용하려면 현재 에디터 확장 `NPCControllerEditor`의 `StartFollowingPlayer(PlayerType)` 호출과 `NPCDestinationSettingTrigger`의 `SetTarget*` legacy 호출을 먼저 제거하거나 후속 호환 메서드를 임시로 추가해야 한다.

## Verification Checklist

- NPC `NetworkObject`가 `Is Master Client`이고 `Allow State Authority Override`가 꺼져 있는지 확인한다.
- Master Client에서만 `NavMeshAgent.enabled == true`가 되는지 확인한다.
- Master Client 변경 후 NPC가 기존 `TargetMode`와 ID를 기준으로 이동을 계속하는지 확인한다.
- 로컬 플레이어가 NPC 어깨를 잡으면 자기 `PlayerRef`로만 추적 요청이 승인되는지 확인한다.
- 임의 클라이언트가 다른 플레이어 `PlayerRef`로 `RequestFollowPlayer()`를 호출해도 무시되는지 확인한다.
- 목적지/사망 트리거는 Master Client 권위에서 한 번만 상태를 확정하는지 확인한다.
- NPC가 문에 도착하면 `NetworkDoor.Open()`이 Master Client 권위 문에서 한 번만 실행되는지 확인한다.
- 사망/대사 이벤트가 `DeathEventId`, `DialogueEventId` 증가마다 각 클라이언트에서 한 번만 재생되는지 확인한다.
