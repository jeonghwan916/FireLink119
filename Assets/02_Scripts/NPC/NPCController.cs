using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class NPCController : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform _currentTarget;
    [SerializeField] private Transform _followablePlayer1;
    [SerializeField] private Transform _followablePlayer2;

    [Header("Movement")]
    [SerializeField] private float _walkSpeed = 2f;
    [SerializeField] private float _runSpeed = 6f;
    [SerializeField] private float _crouchWalkSpeed = 2f;
    [SerializeField] private float _stopDistance = 1.5f;
    [SerializeField] private float _runDistance = 6f;
    [SerializeField] private float _repathInterval = 0.1f;
    [SerializeField] private float _currentTargetMoveThreshold = 0.25f;
    [SerializeField] private float _currentTargetSampleRadius = 2f;

    [Header("Animation")]
    [SerializeField] private float _animationDampTime = 0.12f;
    [SerializeField] private float _openDoorCancelTransitionDuration = 0.05f;

    [Header("State")]
    [SerializeField] private NPCState _state = NPCState.Idle;
    [SerializeField] private bool _isCrouching;

    [Header("Door")]
    [SerializeField] private GameObject _currentOpeningDoor;
    
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

    private Vector3 _lastDestination = Vector3.positiveInfinity;
    private float _nextRepathTime;
    private float _currentMoveSpeed;
    private Transform _doorTarget;
    private Transform _finalDestination;
    private bool _isOpeningDoor;
    private bool _isDead;


    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponent<Animator>();

        _agent.stoppingDistance = _stopDistance;
        _agent.updateRotation = true;

        SetMoveSpeed(0f);
        
        // 디버깅용 초기화. Player1을 따라가라고 초기화 세팅
        StartFollowingPlayer(PlayerType.Player1);
    }

    private void Update()
    {
        if (_isDead) return;
        
        UpdateState();
        UpdateDestination();
        UpdateMoveSpeed();
        UpdateAnimation();
    }

    private void UpdateState()
    {
        // Door routes are advanced by arrival and animation completion, not by trigger interrupts.
        if (_isOpeningDoor || !HasArrived())
        {
            return;
        }

        if (_state == NPCState.GoingDoor)
        {
            BeginOpeningDoor();
            return;
        }

        if (_state == NPCState.GoingFinalDestination)
        {
            CompleteRoute();
        }
    }

    private void UpdateDestination()
    {
        if (_isOpeningDoor)
        {
            return;
        }

        if (_currentTarget == null)
        {
            StopMoving();
            return;
        }

        if (Time.time < _nextRepathTime)
        {
            return;
        }

        Vector3 targetPosition = _currentTarget.position;

        if ((_lastDestination - targetPosition).sqrMagnitude < _currentTargetMoveThreshold * _currentTargetMoveThreshold)
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

        _nextRepathTime = Time.time + _repathInterval;
    }

    private void UpdateMoveSpeed()
    {
        if (_isOpeningDoor)
        {
            SetMoveSpeed(0f);
            return;
        }

        if (!IsMovingToTarget())
        {
            SetMoveSpeed(0f);
            return;
        }

        float targetSpeed = _isCrouching ? _crouchWalkSpeed : ShouldRun() ? _runSpeed : _walkSpeed;
        SetMoveSpeed(targetSpeed);
    }

    private bool ShouldRun()
    {
        if (_agent.pathPending)
        {
            return false;
        }

        return _agent.remainingDistance > _runDistance;
    }

    private bool IsMovingToTarget()
    {
        if (!_agent.hasPath || _agent.pathPending)
        {
            return false;
        }

        return _agent.remainingDistance > _agent.stoppingDistance;
    }

    private bool HasArrived()
    {
        if (_currentTarget == null || _agent.pathPending)
        {
            return false;
        }

        if (_agent.hasPath)
        {
            return _agent.remainingDistance <= _agent.stoppingDistance;
        }

        return Vector3.Distance(transform.position, _currentTarget.position) <= _agent.stoppingDistance;
    }

    private void SetMoveSpeed(float speed)
    {
        _currentMoveSpeed = speed;
        _agent.speed = speed;
    }

    private void StopMoving()
    {
        if (_agent.hasPath)
        {
            _agent.ResetPath();
        }

        _lastDestination = Vector3.positiveInfinity;
        SetMoveSpeed(0f);
    }

    private void UpdateAnimation()
    {
        bool isMoving = IsMovingToTarget() && _agent.velocity.sqrMagnitude > 0.01f;

        float speed = isMoving ? _currentMoveSpeed : 0f;
        float motionSpeed = 1f;

        _animator.SetFloat(SpeedHash, speed, _animationDampTime, Time.deltaTime);
        _animator.SetFloat(MotionSpeedHash, motionSpeed, _animationDampTime, Time.deltaTime);
        _animator.SetBool(GroundedHash, true);
        _animator.SetBool(IsCrouchingHash, _isCrouching);
    }

    public void StartFollowingPlayer(PlayerType playerType)
    {
        ClearRouteTargets();
        _state = NPCState.Follow;
        CancelOpeningDoor();
        if (playerType == PlayerType.Player1)
        {
            SetCurrentTarget(_followablePlayer1);
        }
        else if (playerType == PlayerType.Player2)
        {
            SetCurrentTarget(_followablePlayer2);
        }
    }
    
    public void SetTarget(Transform target)
    {
        ClearRouteTargets();
        CancelOpeningDoor();
        _state = target == null ? NPCState.Idle : NPCState.GoingFinalDestination;
        SetCurrentTarget(target);
    }

    public void SetTargetViaDoor(Transform doorTarget, Transform finalDestination)
    {
        // Keep the route intentionally small: door approach, door animation, final destination.
        if (doorTarget == null)
        {
            SetTarget(finalDestination);
            return;
        }

        if (finalDestination == null)
        {
            SetTarget(doorTarget);
            return;
        }

        CancelOpeningDoor();
        _doorTarget = doorTarget;
        _finalDestination = finalDestination;
        _state = NPCState.GoingDoor;
        _currentOpeningDoor = doorTarget.gameObject;
        SetCurrentTarget(_doorTarget);
    }

    private void SetCurrentTarget(Transform target)
    {
        _currentTarget = target;
        _lastDestination = Vector3.positiveInfinity;
        _nextRepathTime = 0f;
    }

    private void ClearRouteTargets()
    {
        _doorTarget = null;
        _finalDestination = null;
    }

    public void ToggleCrouch()
    {
        _isCrouching = !_isCrouching;
    }

    private void BeginOpeningDoor()
    {
        if (_isOpeningDoor)
        {
            return;
        }

        // Stop the NavMeshAgent before firing the one-shot animation trigger.
        _state = NPCState.OpeningDoor;
        _isOpeningDoor = true;
        StopMoving();
        _animator.SetFloat(SpeedHash, 0f);
        _animator.ResetTrigger(OpenDoorHash);
        _animator.SetTrigger(OpenDoorHash);
    }

    public void FinishOpeningDoor()
    {
        if (!_isOpeningDoor)
        {
            return;
        }

        _isOpeningDoor = false;
        
        // todo : 열고 난 뒤의 문 오브젝트 처리 부분
        _currentOpeningDoor.SetActive(false);

        // Animation completion is the only place that advances from door opening to the final leg.
        if (_state == NPCState.OpeningDoor && _finalDestination != null)
        {
            _state = NPCState.GoingFinalDestination;
            SetCurrentTarget(_finalDestination);
            return;
        }

        CompleteRoute();
    }

    private void CancelOpeningDoor()
    {
        if (!_isOpeningDoor)
        {
            return;
        }

        _isOpeningDoor = false;
        _animator.ResetTrigger(OpenDoorHash);
        _animator.CrossFade(IdleWalkRunBlendHash, _openDoorCancelTransitionDuration);
    }

    private void CompleteRoute()
    {
        StopMoving();
        ClearRouteTargets();
        SetCurrentTarget(null);
        _state = NPCState.Idle;
        
        Debug.Log("Final Destination 도착!");
    }

    public void DieByExplosion()
    {
        Die(DeathByExplosionHash);
    }

    public void DieBySmoke()
    {
        Die(DeathBySmokeHash);
    }

    private void Die(int deathTriggerHash)
    {
        if (_isDead)
        {
            return;
        }

        _isDead = true;
        _state = NPCState.Dead;

        _isOpeningDoor = false;
        ClearRouteTargets();
        SetCurrentTarget(null);
        StopMoving();

        _agent.isStopped = true;

        _animator.ResetTrigger(OpenDoorHash);
        _animator.SetFloat(SpeedHash, 0f);
        _animator.SetFloat(MotionSpeedHash, 0f);
        _animator.SetBool(IsCrouchingHash, false);

        _animator.SetTrigger(deathTriggerHash);
    }
    
}
