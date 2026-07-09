using System.Collections;
using UnityEngine;

namespace FireLink119.Ending
{
    [RequireComponent(typeof(AudioSource))]
    public class EndingMusicPlayer : MonoBehaviour
    {
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _musicClip;
        [SerializeField] private float _volume = 0.45f;
        [SerializeField] private bool _loop = true;

        private void Awake()
        {
            if (_audioSource == null)
            {
                _audioSource = GetComponent<AudioSource>();
            }
        }

        private void Start()
        {
            StartCoroutine(PlayAfterAudioListenerReady());
        }

        private IEnumerator PlayAfterAudioListenerReady()
        {
            yield return null;

            if (_audioSource == null)
            {
                Debug.LogWarning("[EndingMusicPlayer] AudioSource is missing.", this);
                yield break;
            }

            AudioClip clip = _musicClip != null ? _musicClip : _audioSource.clip;
            if (clip == null)
            {
                Debug.LogWarning("[EndingMusicPlayer] Music clip is missing.", this);
                yield break;
            }

            _audioSource.clip = clip;
            _audioSource.volume = _volume;
            _audioSource.loop = _loop;
            _audioSource.spatialBlend = 0f;
            _audioSource.dopplerLevel = 0f;

            if (!_audioSource.isPlaying)
            {
                _audioSource.Play();
            }
        }
    }
}
