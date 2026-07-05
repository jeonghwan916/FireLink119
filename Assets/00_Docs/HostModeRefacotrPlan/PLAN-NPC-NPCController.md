# NPCController.cs
```csharp
using Fusion;
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

    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Animator))]
    public class NPCController : NetworkBehaviour
    {
        private const int InvalidTargetId = -1;

        [Header("Legacy Follow Targets")]
        [SerializeField] private Transform _followablePlayer1;
        [SerializeField] private Transform _followablePlayer2;

        [Header("Network Targets")]
        [SerializeField] private Transform[] _destinationTargets;
        [SerializeField] private Transform[] _doorTargets;

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
        [Networked] private int CurrentDoorId { get; set; }
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
        private Transform _finalDestination;
        private Vector3 _lastDestination = Vector3.positiveInfinity;
        private float _nextRepathTime;
        private int _lastHandledDialogueEventId;
        private int _lastHandledDeathEventId;
        private bool _hasPlayedOpeningDoorTrigger;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _animator = GetComponent<Animator>();
            _audioSource = GetComponent<AudioSource>();

            if (_agent != null)
            {
                _agent.updateRotation = true;
            }
        }

        public override void Spawned()
        {
            if (HasStateAuthority)
            {
                State = _initialState;
                IsCrouching = _initialIsCrouching;
                IsDead = false;
                IsOpeningDoor = false;
                NetworkPosition = transform.position;
                NetworkRotation = transform.rotation;
                NetworkMoveSpeed = 0f;
                CurrentDoorId = InvalidTargetId;
                DialogueClipIndex = InvalidTargetId;
                IsCalmDownDialogue = false;
                DialogueEventId = 0;
                DeathType = NPCDeathType.None;
                DeathEventId = 0;

                ApplyStopDistanceForState();
                SetAuthorityMoveSpeed(0f);
            }

            ConfigureAgentForAuthority();
            ApplyNetworkPose();
            ApplyNetworkAnimation();
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || IsDead)
            {
                return;
            }

            UpdateAuthorityState();
            UpdateAuthorityDestination();
            UpdateAuthorityMoveSpeed();
            WriteNetworkPose();
        }

        public override void Render()
        {
            ApplyNetworkPose();
            ApplyNetworkAnimation();
            ApplyNetworkEvents();
        }

        public void RequestFollowPlayer(PlayerRef player)
        {
            if (HasStateAuthority)
            {
                ApplyFollowPlayer(player);
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

        // Legacy PlayerType follow targets should not be used by new networked callers.
        // Keep this only while old scene references are being migrated to PlayerRef.
        public void StartFollowingPlayer(PlayerType playerType)
        {
            if (!HasStateAuthority)
            {
                Debug.LogWarning("[NPCController] StartFollowingPlayer(PlayerType) is legacy-only. Use RequestFollowPlayer(PlayerRef).");
                return;
            }

            ApplyLegacyFollowPlayer(playerType);
        }

        // TODO: Destination trigger가 ID 기반 요청만 사용하게 되면 이 legacy Transform 경로는 제거한다.
        public void SetTarget(Transform target)
        {
            if (!HasStateAuthority)
            {
                Debug.LogWarning("[NPCController] SetTarget(Transform) must be replaced by RequestSetDestination(int) for networked callers.");
                return;
            }

            ApplyDestinationTransform(target);
        }

        // TODO: Destination trigger가 ID 기반 요청만 사용하게 되면 이 legacy Transform 경로는 제거한다.
        public void SetTargetViaDoor(Transform doorTarget, Transform finalDestination)
        {
            if (!HasStateAuthority)
            {
                Debug.LogWarning("[NPCController] SetTargetViaDoor(Transform, Transform) must be replaced by RequestSetDestinationViaDoor(int, int) for networked callers.");
                return;
            }

            ApplyDestinationViaDoorTransform(doorTarget, finalDestination);
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
            if (!HasStateAuthority || !IsOpeningDoor)
            {
                return;
            }

            CompleteOpeningDoor();
        }

        public void DieByExplosion()
        {
            RequestDie(NPCDeathType.Explosion);
        }

        public void DieBySmoke()
        {
            RequestDie(NPCDeathType.Smoke);
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

        public void PlayDialogue(AudioClip clip, string text)
        {
            if (_audioSource == null || clip == null)
            {
                return;
            }

            _audioSource.PlayOneShot(clip);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestFollowPlayer(PlayerRef player, RpcInfo info = default)
        {
            if (!CanRequesterControlPlayer(player, info.Source))
            {
                return;
            }

            ApplyFollowPlayer(player);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestSetDestination(int destinationId, RpcInfo info = default)
        {
            if (!CanAcceptWorldStateRequest(info.Source))
            {
                return;
            }

            ApplyDestination(destinationId);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestSetDestinationViaDoor(int doorId, int destinationId, RpcInfo info = default)
        {
            if (!CanAcceptWorldStateRequest(info.Source))
            {
                return;
            }

            ApplyDestinationViaDoor(doorId, destinationId);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestToggleCrouch(RpcInfo info = default)
        {
            if (!CanAcceptWorldStateRequest(info.Source))
            {
                return;
            }

            ApplyToggleCrouch();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestDie(NPCDeathType deathType, RpcInfo info = default)
        {
            if (!CanAcceptWorldStateRequest(info.Source))
            {
                return;
            }

            ApplyDeath(deathType);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestPlayDialogue(int dialogueId, RpcInfo info = default)
        {
            if (!CanAcceptWorldStateRequest(info.Source))
            {
                return;
            }

            ApplyDialogue(dialogueId);
        }

        private void ConfigureAgentForAuthority()
        {
            if (_agent == null)
            {
                return;
            }

            if (HasStateAuthority)
            {
                _agent.enabled = true;
                _agent.updatePosition = true;
                _agent.updateRotation = true;
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

        private bool ShouldRun()
        {
            if (_agent == null || _agent.pathPending)
            {
                return false;
            }

            return _agent.remainingDistance > _runDistance;
        }

        private bool IsMovingToTarget()
        {
            if (_agent == null || !_agent.enabled || !_agent.hasPath || _agent.pathPending)
            {
                return false;
            }

            return _agent.remainingDistance > _agent.stoppingDistance;
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

            switch (State)
            {
                case NPCState.GoingDoor:
                    _agent.stoppingDistance = _doorStopDistance;
                    break;
                case NPCState.Idle:
                case NPCState.Follow:
                case NPCState.OpeningDoor:
                case NPCState.GoingFinalDestination:
                case NPCState.Dead:
                    _agent.stoppingDistance = _normalStopDistance;
                    break;
            }
        }

        private void ApplyFollowPlayer(PlayerRef player)
        {
            Transform target = ResolveFollowTarget(player);
            if (target == null)
            {
                Debug.LogWarning($"[NPCController] Could not resolve player object for {player}.");
                return;
            }

            bool wasInterrupted = IsRouteInProgress();

            if (wasInterrupted)
            {
                ApplyRandomCalmDownDialogue();
            }

            ClearRouteTargets();
            CancelOpeningDoor();

            SetState(NPCState.Follow);
            SetCurrentTarget(target);
        }

        private void ApplyLegacyFollowPlayer(PlayerType playerType)
        {
            bool wasInterrupted = IsRouteInProgress();

            if (wasInterrupted)
            {
                ApplyRandomCalmDownDialogue();
            }

            ClearRouteTargets();
            CancelOpeningDoor();

            SetState(NPCState.Follow);
            SetCurrentTarget(ResolveLegacyFollowTarget(playerType));
        }

        private void ApplyDestination(int destinationId)
        {
            Transform destination = ResolveDestinationTarget(destinationId);

            ClearRouteTargets();
            CancelOpeningDoor();

            CurrentDoorId = InvalidTargetId;

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

            CurrentDoorId = doorId;
            _finalDestination = finalDestination;

            SetState(NPCState.GoingDoor);
            SetCurrentTarget(doorTarget);
        }

        // TODO: Destination trigger가 ID 기반 요청만 사용하게 되면 이 legacy Transform 경로는 제거한다.
        private void ApplyDestinationTransform(Transform target)
        {
            ClearRouteTargets();
            CancelOpeningDoor();

            CurrentDoorId = InvalidTargetId;

            SetState(target == null ? NPCState.Idle : NPCState.GoingFinalDestination);
            SetCurrentTarget(target);
        }

        // TODO: Destination trigger가 ID 기반 요청만 사용하게 되면 이 legacy Transform 경로는 제거한다.
        private void ApplyDestinationViaDoorTransform(Transform doorTarget, Transform finalDestination)
        {
            if (doorTarget == null)
            {
                ApplyDestinationTransform(finalDestination);
                return;
            }

            CancelOpeningDoor();

            CurrentDoorId = InvalidTargetId;
            _finalDestination = finalDestination;

            SetState(NPCState.GoingDoor);
            SetCurrentTarget(doorTarget);
        }

        private void ApplyToggleCrouch()
        {
            if (IsDead)
            {
                return;
            }

            IsCrouching = !IsCrouching;
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

            if (State == NPCState.OpeningDoor && _finalDestination != null)
            {
                SetState(NPCState.GoingFinalDestination);
                SetCurrentTarget(_finalDestination);
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
            ClearRouteTargets();
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
            ClearRouteTargets();
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
            if (requester == default)
            {
                return true;
            }

            return requester == requestedPlayer;
        }

        private bool CanAcceptWorldStateRequest(PlayerRef requester)
        {
            // Host-owned triggers call the Apply* path directly. Client RPCs that change shared
            // NPC/world state should be accepted only after a real game-specific validation is added.
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

        private void ClearRouteTargets()
        {
            _finalDestination = null;
        }

        private Transform ResolveFollowTarget(PlayerRef player)
        {
            if (Runner == null)
            {
                return null;
            }

            NetworkObject playerObject = Runner.GetPlayerObject(player);
            return playerObject != null ? playerObject.transform : null;
        }

        private Transform ResolveLegacyFollowTarget(PlayerType playerType)
        {
            if (playerType == PlayerType.Player1)
            {
                return _followablePlayer1;
            }

            if (playerType == PlayerType.Player2)
            {
                return _followablePlayer2;
            }

            return null;
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
            TryOpenNetworkDoor(CurrentDoorId);
        }

        private bool TryOpenNetworkDoor(int doorId)
        {
            // Required for PLAN.md compliance: doorId must resolve to a separate networked door
            // component and set its replicated open state here. Do not call SetActive locally.
            return false;
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
            if (HasStateAuthority)
            {
                return;
            }

            transform.SetPositionAndRotation(NetworkPosition, NetworkRotation);
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
