using System;
using System.IO;
using FireLink119.Player;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FireLink119.Network
{
    public class NetworkPlayerAvatar : NetworkBehaviour
    {
        public static event Action RoomGameStartApproved;

        [SerializeField] private Transform _avatarRoot;
        [SerializeField] private Animator _animator;
        [SerializeField] private PlayerAvatarHandTargets _handTargets;
        [SerializeField] private bool _hideForInputAuthority = true;
        [SerializeField] private float _parameterDampTime = 0.1f;

        [Header("Room Start")]
        [SerializeField] private float _roomStartPressWindowSeconds = 1.5f;
        [SerializeField] private string _gameSceneName = "GameScenes";

        [Header("Animator Parameters")]
        [SerializeField] private string _isMovingParameter = "IsMoving";
        [SerializeField] private string _isSprintingParameter = "IsSprinting";
        [SerializeField] private string _moveXParameter = "MoveX";
        [SerializeField] private string _moveYParameter = "MoveY";

        [Networked] private Vector3 AvatarPosition { get; set; }
        [Networked] private Quaternion AvatarRotation { get; set; }
        [Networked] private Vector3 LeftHandLocalPosition { get; set; }
        [Networked] private Quaternion LeftHandLocalRotation { get; set; }
        [Networked] private Vector3 RightHandLocalPosition { get; set; }
        [Networked] private Quaternion RightHandLocalRotation { get; set; }
        [Networked] private Vector2 MoveBlend { get; set; }
        [Networked] private NetworkBool IsMoving { get; set; }
        [Networked] private NetworkBool IsSprinting { get; set; }

        private bool _hasIsMovingParameter;
        private bool _hasIsSprintingParameter;
        private bool _hasMoveXParameter;
        private bool _hasMoveYParameter;

        private static NetworkRunner _roomStartRunner;
        private static float _hostRoomStartPressedTime = float.NegativeInfinity;
        private static float _clientRoomStartPressedTime = float.NegativeInfinity;
        private static bool _isRoomGameStartApproved;
        private static bool _isGameSceneLoadRequested;

        private void Awake()
        {
            // 프리팹 세팅 누락 시에도 런타임에서 최소 참조를 복구해 테스트 중 null 예외를 줄인다.
            if (_avatarRoot == null)
            {
                _avatarRoot = transform;
            }

            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }

            if (_handTargets == null)
            {
                _handTargets = GetComponent<PlayerAvatarHandTargets>();
            }

            if (_handTargets == null)
            {
                _handTargets = GetComponentInChildren<PlayerAvatarHandTargets>();
            }

            if (_handTargets == null)
            {
                // NetworkPlayerAvatar 프리팹 안에 이름이 맞는 Target이 있으면 helper가 런타임에 찾아 적용한다.
                _handTargets = gameObject.AddComponent<PlayerAvatarHandTargets>();
            }

            CacheAnimatorParameters();
        }

        public override void Spawned()
        {
            // 네트워크 스폰 직후 초기 위치를 기록해 첫 렌더 프레임에서 원점에 잠깐 보이는 현상을 줄인다.
            CacheAnimatorParameters();
            EnsureRoomStartStateForCurrentRunner();

            if (HasStateAuthority)
            {
                AvatarPosition = _avatarRoot.position;
                AvatarRotation = _avatarRoot.rotation;
            }

            ApplyInputAuthorityVisibility();
        }

        public void RequestRoomStartButtonPress(LobbyRoomRole buttonRole)
        {
            if (!HasInputAuthority)
            {
                return;
            }

            RPC_RequestRoomStartButtonPress((int)buttonRole);
        }

        public void RequestEmergencyDoorPush(int doorId, float openAngleDelta)
        {
            if (!HasInputAuthority)
            {
                return;
            }

            RPC_RequestEmergencyDoorPush(doorId, openAngleDelta);
        }

        public override void FixedUpdateNetwork()
        {
            // StateAuthority만 입력을 읽어 Networked 상태를 갱신한다. 다른 클라이언트는 복제된 값을 Render에서 적용한다.
            if (!HasStateAuthority)
            {
                return;
            }

            if (!GetInput(out VrAvatarNetworkInput input))
            {
                return;
            }

            AvatarPosition = input.AvatarPosition;
            AvatarRotation = input.AvatarRotation;
            LeftHandLocalPosition = input.LeftHandLocalPosition;
            LeftHandLocalRotation = input.LeftHandLocalRotation;
            RightHandLocalPosition = input.RightHandLocalPosition;
            RightHandLocalRotation = input.RightHandLocalRotation;
            MoveBlend = input.MoveBlend;
            IsMoving = input.IsMoving;
            IsSprinting = input.IsSprinting;
        }

        public override void Render()
        {
            // Fusion 보간 렌더 단계에서 아바타 루트, 손 IK Target, Animator 값을 모두 반영한다.
            ApplyNetworkPose();
            ApplyHandTargets();
            ApplyAnimatorParameters();
        }

        private void ApplyNetworkPose()
        {
            if (_avatarRoot == null)
            {
                return;
            }

            _avatarRoot.SetPositionAndRotation(AvatarPosition, AvatarRotation);
        }

        private void ApplyHandTargets()
        {
            // 손 Target을 움직이면 프리팹의 Rig_Arms/Two Bone IK가 실제 팔 본을 계산한다.
            if (_handTargets == null)
            {
                return;
            }

            _handTargets.ApplyLocalHandTargets(
                LeftHandLocalPosition,
                LeftHandLocalRotation,
                RightHandLocalPosition,
                RightHandLocalRotation);
        }

        private void ApplyAnimatorParameters()
        {
            if (_animator == null)
            {
                return;
            }

            if (_hasIsMovingParameter)
            {
                _animator.SetBool(_isMovingParameter, IsMoving);
            }

            if (_hasIsSprintingParameter)
            {
                _animator.SetBool(_isSprintingParameter, IsSprinting);
            }

            if (_hasMoveXParameter)
            {
                _animator.SetFloat(_moveXParameter, MoveBlend.x, _parameterDampTime, Time.deltaTime);
            }

            if (_hasMoveYParameter)
            {
                _animator.SetFloat(_moveYParameter, MoveBlend.y, _parameterDampTime, Time.deltaTime);
            }
        }

        private void ApplyInputAuthorityVisibility()
        {
            // 자신의 실제 1인칭 몸은 XR Origin 안의 로컬 아바타가 담당하므로, 네트워크 프리팹의 자기 복제본은 숨긴다.
            if (!_hideForInputAuthority || !HasInputAuthority)
            {
                return;
            }

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = false;
            }
        }

        private void CacheAnimatorParameters()
        {
            // Animator Controller가 바뀌어도 없는 파라미터를 Set하지 않도록 미리 존재 여부를 캐시한다.
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

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RPC_RequestRoomStartButtonPress(int buttonRoleValue, RpcInfo info = default)
        {
            EnsureRoomStartStateForCurrentRunner();

            if (_isRoomGameStartApproved)
            {
                return;
            }

            if (!TryConvertRoomRole(buttonRoleValue, out LobbyRoomRole requestedRole))
            {
                Debug.LogWarning($"[NetworkPlayerAvatar] Invalid room start button role: {buttonRoleValue}");
                return;
            }

            LobbyRoomRole expectedRole = GetExpectedRoleForInputAuthority();
            if (requestedRole != expectedRole)
            {
                Debug.LogWarning($"[NetworkPlayerAvatar] Ignored room start press. Expected: {expectedRole}, Requested: {requestedRole}");
                return;
            }

            float pressedTime = Time.time;
            if (requestedRole == LobbyRoomRole.Host)
            {
                _hostRoomStartPressedTime = pressedTime;
            }
            else
            {
                _clientRoomStartPressedTime = pressedTime;
            }

            Debug.Log($"[NetworkPlayerAvatar] Room start button pressed. Role: {requestedRole}");
            TryApproveRoomGameStart();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_NotifyRoomGameStartApproved()
        {
            Debug.Log("[NetworkPlayerAvatar] Room game start approved.");
            RoomGameStartApproved?.Invoke();

            if (HasStateAuthority)
            {
                TryLoadGameScene();
            }
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RPC_RequestEmergencyDoorPush(int doorId, float openAngleDelta, RpcInfo info = default)
        {
            float safeOpenAngleDelta = Mathf.Clamp(openAngleDelta, -5f, 5f);
            if (!NetworkEmergencyDoor.TryApplyPushAsStateAuthority(doorId, safeOpenAngleDelta, out float openAngle))
            {
                Debug.LogWarning($"[NetworkPlayerAvatar] Emergency door not found. DoorId: {doorId}");
                return;
            }

            RPC_NotifyEmergencyDoorAngle(doorId, openAngle);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_NotifyEmergencyDoorAngle(int doorId, float openAngle)
        {
            NetworkEmergencyDoor.ApplyNetworkAngle(doorId, openAngle);
        }

        private void TryApproveRoomGameStart()
        {
            if (_isRoomGameStartApproved)
            {
                return;
            }

            float timeDifference = Mathf.Abs(_hostRoomStartPressedTime - _clientRoomStartPressedTime);
            if (timeDifference > _roomStartPressWindowSeconds)
            {
                return;
            }

            _isRoomGameStartApproved = true;
            RPC_NotifyRoomGameStartApproved();
        }

        private void TryLoadGameScene()
        {
            if (_isGameSceneLoadRequested)
            {
                return;
            }

            if (Runner == null)
            {
                Debug.LogWarning("[NetworkPlayerAvatar] NetworkRunner is required to load the game scene.");
                return;
            }

            int gameSceneBuildIndex = GetBuildIndexBySceneName(_gameSceneName);
            if (gameSceneBuildIndex < 0)
            {
                Debug.LogError($"[NetworkPlayerAvatar] Game scene is not registered in Build Settings: {_gameSceneName}");
                return;
            }

            _isGameSceneLoadRequested = true;
            Runner.LoadScene(SceneRef.FromIndex(gameSceneBuildIndex), LoadSceneMode.Single);
        }

        private LobbyRoomRole GetExpectedRoleForInputAuthority()
        {
            if (Runner != null && Object != null && Object.InputAuthority == Runner.LocalPlayer)
            {
                return LobbyRoomRole.Host;
            }

            return LobbyRoomRole.Client;
        }

        private bool TryConvertRoomRole(int roleValue, out LobbyRoomRole role)
        {
            if (roleValue == (int)LobbyRoomRole.Host)
            {
                role = LobbyRoomRole.Host;
                return true;
            }

            if (roleValue == (int)LobbyRoomRole.Client)
            {
                role = LobbyRoomRole.Client;
                return true;
            }

            role = default;
            return false;
        }

        private void EnsureRoomStartStateForCurrentRunner()
        {
            if (Runner == null || _roomStartRunner == Runner)
            {
                return;
            }

            _roomStartRunner = Runner;
            _hostRoomStartPressedTime = float.NegativeInfinity;
            _clientRoomStartPressedTime = float.NegativeInfinity;
            _isRoomGameStartApproved = false;
            _isGameSceneLoadRequested = false;
        }

        private int GetBuildIndexBySceneName(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return -1;
            }

            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
                string buildSceneName = Path.GetFileNameWithoutExtension(scenePath);

                if (buildSceneName == sceneName)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
