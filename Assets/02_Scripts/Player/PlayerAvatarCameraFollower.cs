using UnityEngine;

namespace FireLink119.Player
{
    [DisallowMultipleComponent]
    public class PlayerAvatarCameraFollower : MonoBehaviour
    {
        [SerializeField] private Transform _cameraTransform;
        [SerializeField] private Transform _visualRoot;
        [SerializeField] private Vector3 _cameraLocalOffset = new Vector3(0f, 0f, -0.25f);
        [SerializeField] private bool _lockInitialHeight = true;
        [SerializeField] private bool _followCameraYaw = true;

        private float _initialVisualRootY;
        private bool _hasInitialVisualRootY;
        private bool _warnedMissingCamera;

        private void Awake()
        {
            // XR Origin 안에서 사용할 때는 보통 이 스크립트가 붙은 아바타 루트를 직접 움직인다.
            if (_visualRoot == null)
            {
                _visualRoot = transform;
            }

            // 씬마다 Main Camera 참조를 다시 연결하지 않아도 기본 XR 카메라를 따라가게 한다.
            if (_cameraTransform == null && Camera.main != null)
            {
                _cameraTransform = Camera.main.transform;
            }
        }

        private void Start()
        {
            CaptureInitialHeight();
        }

        private void LateUpdate()
        {
            // XR 카메라는 프레임 중 추적값으로 갱신되므로, LateUpdate에서 따라가야 HMD 위치와 아바타 위치 차이가 덜 흔들린다.
            if (!CanFollowCamera())
            {
                return;
            }

            Quaternion cameraYawRotation = Quaternion.Euler(0f, _cameraTransform.eulerAngles.y, 0f);
            Vector3 targetPosition = _cameraTransform.position + cameraYawRotation * _cameraLocalOffset;

            if (_lockInitialHeight)
            {
                CaptureInitialHeight();
                targetPosition.y = _initialVisualRootY;
            }

            _visualRoot.position = targetPosition;

            if (_followCameraYaw)
            {
                _visualRoot.rotation = cameraYawRotation;
            }
        }

        private bool CanFollowCamera()
        {
            if (_cameraTransform != null)
            {
                return true;
            }

            if (!_warnedMissingCamera)
            {
                Debug.LogWarning("[PlayerAvatarCameraFollower] Camera Transform is not assigned.");
                _warnedMissingCamera = true;
            }

            return false;
        }

        private void CaptureInitialHeight()
        {
            // 높이를 고정하면 현실에서 머리를 숙이거나 들 때 몸 전체가 위아래로 출렁이는 것을 막을 수 있다.
            if (_hasInitialVisualRootY || _visualRoot == null)
            {
                return;
            }

            _initialVisualRootY = _visualRoot.position.y;
            _hasInitialVisualRootY = true;
        }
    }
}
