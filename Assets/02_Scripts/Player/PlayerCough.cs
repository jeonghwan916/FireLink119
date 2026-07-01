using System.Collections;
using UnityEngine;

namespace FireLink119.Player
{
    public class PlayerCough : MonoBehaviour
    {
        [Header("Audio")]
        [SerializeField] private AudioClip _coughClip;
        private AudioSource _audioSource;
        
        [Header("Status")]
        [SerializeField] private bool _isEnterSmoke = false;
        [SerializeField] private float _coughDelay = 3.0f;
        private Coroutine _coughCoroutine;
        

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            
            _coughCoroutine = null;
        }

        public void StartCough()
        {
            _isEnterSmoke = true;
            
            if (_coughCoroutine == null)
            {
                _coughCoroutine = StartCoroutine(Cough());
            }
        }

        public void StopCough()
        {
            _isEnterSmoke = false;
        }

        IEnumerator Cough()
        {
            while (_isEnterSmoke)
            {
                if (_audioSource != null && _coughClip != null)
                {
                    _audioSource.PlayOneShot(_coughClip);
                }

                yield return new WaitForSeconds(_coughDelay);
            }
            
            _coughCoroutine = null;
        }
    }
}
