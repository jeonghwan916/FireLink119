using UnityEngine;

namespace FireLink119.Player
{
    public static class LocalNarrationAudio
    {
        private const string AudioSourceName = "Local Narration Audio";

        private static AudioSource _audioSource;

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

            source.PlayOneShot(clip, volume);
        }

        private static AudioSource GetOrCreateAudioSource()
        {
            if (_audioSource != null)
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
            _audioSource.priority = 64;

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
    }
}
