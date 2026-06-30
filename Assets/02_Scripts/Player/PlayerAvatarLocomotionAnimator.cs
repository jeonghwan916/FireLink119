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

        private void Awake()
        {
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
            ResolveMoveAction();
            ResolveSprintAction();
            CacheAnimatorParameters();

            // XRI Input Action Manager may already control action lifetime, so only disable actions this script enabled.
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

            ApplyAnimatorParameters(moveBlend, isMoving, isSprinting);
        }

        private void ResolveMoveAction()
        {
            _moveAction = _moveActionReference != null ? _moveActionReference.action : null;
        }

        private void ResolveSprintAction()
        {
            _sprintAction = _sprintActionReference != null ? _sprintActionReference.action : null;
        }

        private bool CanUpdateAnimation()
        {
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

            // Match the OneShot Blend Tree convention: local X/Z movement drives MoveX/MoveY.
            return localBlend * normalizedMoveAmount;
        }

        private void ApplyAnimatorParameters(Vector2 moveBlend, bool isMoving, bool isSprinting)
        {
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
