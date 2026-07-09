using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FireLink119.Player
{
    public static class LocalNarrationAudio
    {
        private const string AudioSourceName = "Local Narration Audio";
        private const float MasterVolumeMultiplier = 1.8f;
        private const int AudioPriority = 16;

        private static AudioSource _audioSource;
        private static NarrationQueuePlayer _queuePlayer;

        public static void PlayOneShot(AudioClip clip, float volume = 1f)
        {
            if (clip == null)
            {
                return;
            }

            AudioSource source = GetOrCreateAudioSource();
            if (source == null)
            {
                return;
            }

            float scaledVolume = Mathf.Max(0f, volume) * MasterVolumeMultiplier;
            _queuePlayer.Enqueue(clip, scaledVolume);
        }

        private static AudioSource GetOrCreateAudioSource()
        {
            if (_audioSource != null && _queuePlayer != null)
            {
                return _audioSource;
            }

            Transform listenerTransform = ResolveListenerTransform();
            GameObject audioObject = new GameObject(AudioSourceName);

            if (listenerTransform != null)
            {
                audioObject.transform.SetParent(listenerTransform, false);
                audioObject.transform.localPosition = Vector3.zero;
                audioObject.transform.localRotation = Quaternion.identity;
            }

            _audioSource = audioObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.spatialBlend = 0f;
            _audioSource.dopplerLevel = 0f;
            _audioSource.priority = AudioPriority;

            _queuePlayer = audioObject.AddComponent<NarrationQueuePlayer>();
            _queuePlayer.Initialize(_audioSource);

            return _audioSource;
        }

        private static Transform ResolveListenerTransform()
        {
            AudioListener listener = Object.FindFirstObjectByType<AudioListener>();
            if (listener != null)
            {
                return listener.transform;
            }

            Camera mainCamera = Camera.main;
            return mainCamera != null ? mainCamera.transform : null;
        }

        private sealed class NarrationQueuePlayer : MonoBehaviour
        {
            private readonly Queue<NarrationRequest> _requests = new Queue<NarrationRequest>();

            private AudioSource _source;
            private Coroutine _playbackCoroutine;

            public void Initialize(AudioSource source)
            {
                _source = source;
            }

            public void Enqueue(AudioClip clip, float volume)
            {
                _requests.Enqueue(new NarrationRequest(clip, volume));

                if (_playbackCoroutine == null)
                {
                    _playbackCoroutine = StartCoroutine(PlayQueuedNarrations());
                }
            }

            private IEnumerator PlayQueuedNarrations()
            {
                while (_requests.Count > 0)
                {
                    NarrationRequest request = _requests.Dequeue();
                    if (_source == null || request.Clip == null)
                    {
                        continue;
                    }

                    _source.Stop();
                    _source.clip = request.Clip;
                    _source.volume = request.Volume;
                    _source.Play();

                    while (_source != null && _source.isPlaying)
                    {
                        yield return null;
                    }
                }

                _playbackCoroutine = null;
            }
        }

        private readonly struct NarrationRequest
        {
            public NarrationRequest(AudioClip clip, float volume)
            {
                Clip = clip;
                Volume = volume;
            }

            public AudioClip Clip { get; }
            public float Volume { get; }
        }
    }
}
