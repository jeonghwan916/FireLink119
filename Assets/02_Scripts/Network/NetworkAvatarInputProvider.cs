using FireLink119.Player;
using Fusion;
using UnityEngine;

namespace FireLink119.Network
{
    public class NetworkAvatarInputProvider : NetworkRunnerCallbacksBehaviour
    {
        private PlayerAvatarLocomotionAnimator _cachedAnimator;
        private PlayerAvatarHandTargets _cachedHandTargets;
        private bool _warnedMissingLocalAvatar;
        private bool _warnedMissingHandTargets;

        public override void OnInput(NetworkRunner runner, NetworkInput input)
        {
            // Fusion은 입력 권한이 있는 클라이언트의 OnInput에서 데이터를 수집하므로, 여기서 로컬 XR 아바타 상태를 패킷으로 만든다.
            if (!TryGetLocalAvatar(out PlayerAvatarLocomotionAnimator animator))
            {
                return;
            }

            Transform avatarRoot = animator.AvatarRoot;
            TryGetLocalHandTargets(animator, out PlayerAvatarHandTargets handTargets);

            VrAvatarNetworkInput avatarInput = new VrAvatarNetworkInput
            {
                AvatarPosition = avatarRoot.position,
                AvatarRotation = avatarRoot.rotation,
                MoveBlend = animator.CurrentMoveBlend,
                IsMoving = animator.IsMoving,
                IsSprinting = animator.IsSprinting
            };

            // 손은 본을 직접 보내지 않고 IK Target을 보낸다. 그래야 상대방 프리팹에서도 같은 IK 리그가 자연스럽게 팔을 계산한다.
            if (handTargets != null && handTargets.TryGetLocalHandTargets(
                out Vector3 leftHandPosition,
                out Quaternion leftHandRotation,
                out Vector3 rightHandPosition,
                out Quaternion rightHandRotation))
            {
                avatarInput.LeftHandLocalPosition = leftHandPosition;
                avatarInput.LeftHandLocalRotation = leftHandRotation;
                avatarInput.RightHandLocalPosition = rightHandPosition;
                avatarInput.RightHandLocalRotation = rightHandRotation;
            }

            input.Set(avatarInput);
        }

        private bool TryGetLocalAvatar(out PlayerAvatarLocomotionAnimator animator)
        {
            // 매 tick마다 Find를 호출하지 않도록 캐시하되, 씬 전환 후 오브젝트가 바뀌면 다시 탐색한다.
            if (_cachedAnimator != null && _cachedAnimator.isActiveAndEnabled)
            {
                animator = _cachedAnimator;
                return true;
            }

            _cachedAnimator = FindFirstObjectByType<PlayerAvatarLocomotionAnimator>();
            if (_cachedAnimator != null)
            {
                _warnedMissingLocalAvatar = false;
                animator = _cachedAnimator;
                return true;
            }

            if (!_warnedMissingLocalAvatar)
            {
                Debug.LogWarning("[NetworkAvatarInputProvider] Local PlayerAvatarLocomotionAnimator was not found in the loaded scene.");
                _warnedMissingLocalAvatar = true;
            }

            animator = null;
            return false;
        }

        private bool TryGetLocalHandTargets(PlayerAvatarLocomotionAnimator animator, out PlayerAvatarHandTargets handTargets)
        {
            // 손 Target 컴포넌트는 수동 연결도 가능하지만, 현재 씬 작업을 단순하게 유지하기 위해 런타임 자동 탐색도 지원한다.
            if (_cachedHandTargets != null && _cachedHandTargets.isActiveAndEnabled)
            {
                handTargets = _cachedHandTargets;
                return true;
            }

            _cachedHandTargets = animator.GetComponent<PlayerAvatarHandTargets>();

            if (_cachedHandTargets == null)
            {
                _cachedHandTargets = animator.GetComponentInChildren<PlayerAvatarHandTargets>();
            }

            if (_cachedHandTargets == null)
            {
                _cachedHandTargets = animator.GetComponentInParent<PlayerAvatarHandTargets>();
            }

            if (_cachedHandTargets == null)
            {
                // 로컬 아바타 아래 또는 XR Origin 아래에 LeftHandTarget/RightHandTarget 이름이 있으면 helper가 자동으로 찾는다.
                _cachedHandTargets = animator.gameObject.AddComponent<PlayerAvatarHandTargets>();
            }

            if (_cachedHandTargets != null)
            {
                _warnedMissingHandTargets = false;
                handTargets = _cachedHandTargets;
                return true;
            }

            if (!_warnedMissingHandTargets)
            {
                Debug.LogWarning("[NetworkAvatarInputProvider] Local PlayerAvatarHandTargets was not found. Hand IK targets will not be sent.");
                _warnedMissingHandTargets = true;
            }

            handTargets = null;
            return false;
        }
    }
}
