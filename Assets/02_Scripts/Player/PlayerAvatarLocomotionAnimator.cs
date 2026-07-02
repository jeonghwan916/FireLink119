using UnityEngine;
using UnityEngine.InputSystem;

namespace FireLink119.Player
{
    public class PlayerAvatarLocomotionAnimator : MonoBehaviour
    {
        [SerializeField] private Animator _animator;
        [SerializeField] private InputActionReference _moveActionReference;
        [SerializeField] private InputActionReference _sprintActionReference;
        [SerializeField] private Transform _movementReference;
        [SerializeField] private Transform _avatarRoot;
        [SerializeField] private float _moveDeadZone = 0.15f;
        [SerializeField] private float _parameterDampTime = 0.1f;

        [Header("Animator Parameters")]
        [SerializeField] private string _isMovingParameter = "IsMoving";
        [SerializeField] private string _isSprintingParameter = "IsSprinting";
        [SerializeField] private string _moveXParameter = "MoveX";
        [SerializeField] private string _moveYParameter = "MoveY";

        private InputAction _moveAction;
        private InputAction _sprintAction;
        private bool _enabledMoveActionHere;
        private bool _enabledSprintActionHere;
        private bool _hasIsMovingParameter;
        private bool _hasIsSprintingParameter;
        private bool _hasMoveXParameter;
        private bool _hasMoveYParameter;
        private bool _warnedMissingAnimator;
        private bool _warnedMissingMoveAction;

        public Transform AvatarRoot => _avatarRoot != null ? _avatarRoot : transform;
        public Vector2 CurrentMoveBlend { get; private set; }
        public bool IsMoving { get; private set; }
        public bool IsSprinting { get; private set; }

        private void Awake()
        {
            // 프리팹에서 참조를 직접 연결하지 않아도 기본 아바타 구조에서는 자식 Animator를 찾아 사용한다.
            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }

            if (_avatarRoot == null)
            {
                _avatarRoot = transform;
            }

            CacheAnimatorParameters();
        }

        private void OnEnable()
        {
            // XRI Input Action Manager가 이미 Action을 켜는 경우가 있으므로, 이 스크립트가 켠 Action만 나중에 끄도록 기록한다.
            ResolveMoveAction();
            ResolveSprintAction();
            CacheAnimatorParameters();

            if (_moveAction != null && !_moveAction.enabled)
            {
                _moveAction.Enable();
                _enabledMoveActionHere = true;
            }

            if (_sprintAction != null && !_sprintAction.enabled)
            {
                _sprintAction.Enable();
                _enabledSprintActionHere = true;
            }
        }

        private void OnDisable()
        {
            // 다른 XR 시스템이 공유하는 Action을 실수로 끄지 않도록, 이 컴포넌트가 Enable한 경우만 Disable한다.
            if (_moveAction != null && _enabledMoveActionHere)
            {
                _moveAction.Disable();
            }

            if (_sprintAction != null && _enabledSprintActionHere)
            {
                _sprintAction.Disable();
            }

            _enabledMoveActionHere = false;
            _enabledSprintActionHere = false;
        }

        private void Update()
        {
            // 로컬 플레이어의 이동 입력을 Animator 파라미터로 바꾸고, 네트워크 입력 제공자가 읽을 최신 상태도 저장한다.
            if (!CanUpdateAnimation())
            {
                return;
            }

            Vector2 moveInput = _moveAction.ReadValue<Vector2>();
            float moveAmount = Mathf.Clamp01(moveInput.magnitude);
            bool isMoving = moveAmount > _moveDeadZone;
            float normalizedMoveAmount = isMoving ? Mathf.InverseLerp(_moveDeadZone, 1f, moveAmount) : 0f;
            bool isSprinting = isMoving && _sprintAction != null && _sprintAction.IsPressed();
            Vector2 moveBlend = isMoving ? GetLocalMoveBlend(moveInput, normalizedMoveAmount) : Vector2.zero;

            CurrentMoveBlend = moveBlend;
            IsMoving = isMoving;
            IsSprinting = isSprinting;

            ApplyAnimatorParameters(moveBlend, isMoving, isSprinting);
        }

        private void ResolveMoveAction()
        {
            // Inspector의 InputActionReference에서 실제 런타임 Action을 꺼낸다.
            _moveAction = _moveActionReference != null ? _moveActionReference.action : null;
        }

        private void ResolveSprintAction()
        {
            // 스프린트는 선택 기능이므로 참조가 비어 있어도 이동 애니메이션은 계속 동작한다.
            _sprintAction = _sprintActionReference != null ? _sprintActionReference.action : null;
        }

        private bool CanUpdateAnimation()
        {
            // 필수 참조가 없을 때 매 프레임 같은 로그가 쌓이지 않도록 최초 1회만 경고한다.
            if (_animator == null)
            {
                if (!_warnedMissingAnimator)
                {
                    Debug.LogWarning("[PlayerAvatarLocomotionAnimator] Animator is not assigned.");
                    _warnedMissingAnimator = true;
                }

                return false;
            }

            if (_moveAction == null)
            {
                if (!_warnedMissingMoveAction)
                {
                    Debug.LogWarning("[PlayerAvatarLocomotionAnimator] Move InputActionReference is not assigned.");
                    _warnedMissingMoveAction = true;
                }

                return false;
            }

            return true;
        }

        private Vector2 GetLocalMoveBlend(Vector2 moveInput, float normalizedMoveAmount)
        {
            // Blend Tree는 아바타 로컬 X/Z 기준으로 구성되어 있으므로, 카메라 기준 입력을 아바타 기준 방향으로 변환한다.
            if (_movementReference == null)
            {
                Vector2 rawBlend = moveInput.sqrMagnitude > 1f ? moveInput.normalized : moveInput;
                return rawBlend * normalizedMoveAmount;
            }

            Vector3 referenceForward = Vector3.ProjectOnPlane(_movementReference.forward, Vector3.up).normalized;
            Vector3 referenceRight = Vector3.ProjectOnPlane(_movementReference.right, Vector3.up).normalized;
            Vector3 worldMove = referenceRight * moveInput.x + referenceForward * moveInput.y;

            if (worldMove.sqrMagnitude < 0.0001f)
            {
                return Vector2.zero;
            }

            Transform root = _avatarRoot != null ? _avatarRoot : transform;
            Vector3 localMove = root.InverseTransformDirection(worldMove.normalized);
            Vector2 localBlend = new Vector2(localMove.x, localMove.z);

            if (localBlend.sqrMagnitude > 1f)
            {
                localBlend.Normalize();
            }

            // OneShot에서 맞춘 Blend Tree 규칙을 유지한다. local X/Z 이동값이 MoveX/MoveY 파라미터를 구동한다.
            return localBlend * normalizedMoveAmount;
        }

        private void ApplyAnimatorParameters(Vector2 moveBlend, bool isMoving, bool isSprinting)
        {
            // Animator Controller가 아직 다른 버전이어도 없는 파라미터를 건드리지 않아 런타임 오류를 피한다.
            if (_hasIsMovingParameter)
            {
                _animator.SetBool(_isMovingParameter, isMoving);
            }

            if (_hasIsSprintingParameter)
            {
                _animator.SetBool(_isSprintingParameter, isSprinting);
            }

            if (_hasMoveXParameter)
            {
                _animator.SetFloat(_moveXParameter, moveBlend.x, _parameterDampTime, Time.deltaTime);
            }

            if (_hasMoveYParameter)
            {
                _animator.SetFloat(_moveYParameter, moveBlend.y, _parameterDampTime, Time.deltaTime);
            }
        }

        private void CacheAnimatorParameters()
        {
            // 파라미터 존재 여부를 미리 캐시해 Update마다 문자열 기반 검색을 반복하지 않게 한다.
            if (_animator == null || _animator.runtimeAnimatorController == null)
            {
                return;
            }

            _hasIsMovingParameter = HasAnimatorParameter(_isMovingParameter, AnimatorControllerParameterType.Bool);
            _hasIsSprintingParameter = HasAnimatorParameter(_isSprintingParameter, AnimatorControllerParameterType.Bool);
            _hasMoveXParameter = HasAnimatorParameter(_moveXParameter, AnimatorControllerParameterType.Float);
            _hasMoveYParameter = HasAnimatorParameter(_moveYParameter, AnimatorControllerParameterType.Float);
        }

        private bool HasAnimatorParameter(string parameterName, AnimatorControllerParameterType parameterType)
        {
            // 같은 이름이어도 타입이 다르면 SetBool/SetFloat 호출이 실패하므로 이름과 타입을 함께 확인한다.
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            AnimatorControllerParameter[] parameters = _animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == parameterName && parameters[i].type == parameterType)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
