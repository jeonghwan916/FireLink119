using System.Collections;
using UnityEngine;

namespace FireLink119.Player
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public class PlayerCameraHeightCalibrator : MonoBehaviour
    {
        [SerializeField] private Transform _mainCamera;
        [SerializeField] private float _targetCameraHeight = 1.5f;
        [SerializeField] private float _minimumValidHeight = 0.1f;
        [SerializeField] private float _stableHeightTolerance = 0.005f;
        [SerializeField] private int _requiredStableSamples = 3;

        private bool _isCalibrated;
        private bool _warnedMissingReference;
        private bool _hasHeightSample;
        private float _lastCameraHeight;
        private int _stableSampleCount;
        private readonly WaitForEndOfFrame _waitForEndOfFrame = new WaitForEndOfFrame();

        private IEnumerator Start()
        {
            ResolveMissingReferences();
            yield return _waitForEndOfFrame;

            while (!_isCalibrated)
            {
                if (TryGetStableCameraHeight(out float cameraHeight))
                {
                    ApplyCalibration(cameraHeight);
                }

                yield return _waitForEndOfFrame;
            }
        }

        private void ResolveMissingReferences()
        {
            if (_mainCamera == null && Camera.main != null)
            {
                _mainCamera = Camera.main.transform;
            }
        }

        private bool TryGetStableCameraHeight(out float cameraHeight)
        {
            if (!TryGetCurrentCameraHeight(out cameraHeight))
            {
                return false;
            }

            if (!_hasHeightSample)
            {
                _lastCameraHeight = cameraHeight;
                _stableSampleCount = 1;
                _hasHeightSample = true;
                return false;
            }

            if (Mathf.Abs(cameraHeight - _lastCameraHeight) <= _stableHeightTolerance)
            {
                _stableSampleCount++;
            }
            else
            {
                _stableSampleCount = 1;
                _lastCameraHeight = cameraHeight;
            }

            return _stableSampleCount >= _requiredStableSamples;
        }

        private bool TryGetCurrentCameraHeight(out float cameraHeight)
        {
            cameraHeight = 0f;
            Transform heightReference = transform.parent;

            if (_mainCamera == null || heightReference == null)
            {
                WarnMissingReference();
                return false;
            }

            cameraHeight = heightReference.InverseTransformPoint(_mainCamera.position).y;

            if (cameraHeight < _minimumValidHeight)
            {
                return false;
            }

            return true;
        }

        private void ApplyCalibration(float cameraHeight)
        {
            float heightOffset = _targetCameraHeight - cameraHeight;
            Vector3 calibrationOffsetPosition = transform.localPosition;
            calibrationOffsetPosition.y += heightOffset;
            transform.localPosition = calibrationOffsetPosition;
            _isCalibrated = true;
        }

        private void WarnMissingReference()
        {
            if (_warnedMissingReference)
            {
                return;
            }

            Debug.LogWarning("[PlayerCameraHeightCalibrator] Main Camera or height reference is not assigned.");
            _warnedMissingReference = true;
        }
    }
}
