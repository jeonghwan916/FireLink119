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
            if (_visualRoot == null)
            {
                _visualRoot = transform;
            }

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

            // XR camera movement is updated before LateUpdate, so following here keeps the avatar from drifting into the HMD view.
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
            if (_hasInitialVisualRootY || _visualRoot == null)
            {
                return;
            }

            _initialVisualRootY = _visualRoot.position.y;
            _hasInitialVisualRootY = true;
        }
    }
}
