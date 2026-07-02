using UnityEngine;

namespace FireLink119.Player
{
    public class PlayerAvatarHandTargets : MonoBehaviour
    {
        [SerializeField] private Transform _avatarRoot;
        [SerializeField] private Transform _leftHandTarget;
        [SerializeField] private Transform _rightHandTarget;

        public Transform AvatarRoot => _avatarRoot != null ? _avatarRoot : transform;
        public Transform LeftHandTarget => _leftHandTarget;
        public Transform RightHandTarget => _rightHandTarget;

        private void Awake()
        {
            ResolveMissingReferences();
        }

        private void OnValidate()
        {
            ResolveMissingReferences();
        }

        public bool TryGetLocalHandTargets(
            out Vector3 leftHandPosition,
            out Quaternion leftHandRotation,
            out Vector3 rightHandPosition,
            out Quaternion rightHandRotation)
        {
            // 네트워크에는 월드 좌표 대신 아바타 기준 로컬 좌표를 보낸다.
            // 이렇게 해야 방 안 위치가 다른 상대 아바타도 같은 팔 자세를 자기 몸 기준으로 재현한다.
            ResolveMissingReferences();

            if (_avatarRoot == null || _leftHandTarget == null || _rightHandTarget == null)
            {
                leftHandPosition = Vector3.zero;
                leftHandRotation = Quaternion.identity;
                rightHandPosition = Vector3.zero;
                rightHandRotation = Quaternion.identity;
                return false;
            }

            leftHandPosition = _avatarRoot.InverseTransformPoint(_leftHandTarget.position);
            leftHandRotation = Quaternion.Inverse(_avatarRoot.rotation) * _leftHandTarget.rotation;
            rightHandPosition = _avatarRoot.InverseTransformPoint(_rightHandTarget.position);
            rightHandRotation = Quaternion.Inverse(_avatarRoot.rotation) * _rightHandTarget.rotation;
            return true;
        }

        public void ApplyLocalHandTargets(
            Vector3 leftHandPosition,
            Quaternion leftHandRotation,
            Vector3 rightHandPosition,
            Quaternion rightHandRotation)
        {
            // 수신한 로컬 좌표를 현재 네트워크 아바타 루트 기준 월드 좌표로 되돌려 IK Target에 적용한다.
            ResolveMissingReferences();

            if (_avatarRoot == null || _leftHandTarget == null || _rightHandTarget == null)
            {
                return;
            }

            _leftHandTarget.SetPositionAndRotation(
                _avatarRoot.TransformPoint(leftHandPosition),
                _avatarRoot.rotation * leftHandRotation);

            _rightHandTarget.SetPositionAndRotation(
                _avatarRoot.TransformPoint(rightHandPosition),
                _avatarRoot.rotation * rightHandRotation);
        }

        private void ResolveMissingReferences()
        {
            // Inspector 직접 연결을 우선하되, 테스트 중에는 이름만 맞아도 자동으로 찾게 해서 프리팹 세팅 부담을 줄인다.
            if (_avatarRoot == null)
            {
                _avatarRoot = transform;
            }

            if (_leftHandTarget == null)
            {
                _leftHandTarget = FindRelatedTransform("LeftHandTarget");
            }

            if (_rightHandTarget == null)
            {
                _rightHandTarget = FindRelatedTransform("RightHandTarget");
            }
        }

        private Transform FindRelatedTransform(string targetName)
        {
            // 로컬 네트워크 프리팹은 자기 자식에서 찾고, XR Origin 안 로컬 아바타는 루트 전체에서 컨트롤러 자식 Target까지 찾는다.
            Transform localChild = FindChildRecursive(transform, targetName);

            if (localChild != null)
            {
                return localChild;
            }

            Transform root = transform.root;
            return root != null ? FindChildRecursive(root, targetName) : null;
        }

        private Transform FindChildRecursive(Transform root, string targetName)
        {
            if (root.name == targetName)
            {
                return root;
            }

            foreach (Transform child in root)
            {
                Transform result = FindChildRecursive(child, targetName);

                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}
