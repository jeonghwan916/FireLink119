using System.Collections;
using UnityEngine;

namespace FireLink119.Player
{
    public class PlayerCough : MonoBehaviour
    {
        [Header("HMD")]
        [SerializeField] private Transform _hmdCamera;
        [SerializeField] private float _crouchThreshold = 0.35f;
        [SerializeField] private float _calibrationDuration = 1.0f;
        [SerializeField] private float _minimumValidHeight = 0.5f;

        [Header("Audio")]
        [SerializeField] private AudioClip _coughClip;
        private AudioSource _audioSource;

        [Header("Status")]
        [SerializeField] private bool _isEnterSmoke = false;
        [SerializeField] private float _coughDelay = 3.0f;

        private float _standingHeight;
        private bool _isHeightCalibrated;
        private Coroutine _coughCoroutine;

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
        }

        private IEnumerator Start()
        {
            yield return CalibrateStandingHeight();
        }

        private IEnumerator CalibrateStandingHeight()
        {
            if (_hmdCamera == null)
                yield break;

            float elapsed = 0f;
            float heightSum = 0f;
            int sampleCount = 0;

            while (elapsed < _calibrationDuration)
            {
                float height = _hmdCamera.localPosition.y;

                if (height >= _minimumValidHeight)
                {
                    heightSum += height;
                    sampleCount++;
                    elapsed += Time.deltaTime;
                }

                yield return null;
            }

            if (sampleCount > 0)
            {
                _standingHeight = heightSum / sampleCount;
                _isHeightCalibrated = true;
            }
        }

        public void StartCough()
        {
            _isEnterSmoke = true;

            if (_coughCoroutine == null)
            {
                _coughCoroutine = StartCoroutine(CoughRoutine());
            }
        }

        public void StopCough()
        {
            _isEnterSmoke = false;
        }

        private IEnumerator CoughRoutine()
        {
            while (_isEnterSmoke)
            {
                if (!IsCrouching())
                {
                    PlayCough();
                }

                yield return new WaitForSeconds(_coughDelay);
            }

            _coughCoroutine = null;
        }

        private bool IsCrouching()
        {
            if (_hmdCamera == null || !_isHeightCalibrated)
                return false;

            float currentHeight = _hmdCamera.localPosition.y;
            return currentHeight < _standingHeight - _crouchThreshold;
        }

        private void PlayCough()
        {
            if (_audioSource != null && _coughClip != null)
            {
                _audioSource.PlayOneShot(_coughClip);
            }
        }
    }
}