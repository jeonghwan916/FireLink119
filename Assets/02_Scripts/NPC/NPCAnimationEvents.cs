using UnityEngine;
using Random = UnityEngine.Random;

namespace FireLink119.NPC
{
    public class NPCAnimationEvents : MonoBehaviour
    {
        [SerializeField] private AudioClip[] _footstepAudioClips;
        [SerializeField, Range(0f, 1f)] private float _footstepAudioVolume = 0.5f;

        private NPCController _npcController;

        private void Awake()
        {
            _npcController = GetComponentInParent<NPCController>();
        }

        private void OnFootstep(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight <= 0.5f ||
                _footstepAudioClips == null ||
                _footstepAudioClips.Length == 0)
            {
                return;
            }

            if (_npcController != null && _npcController.IsDead)
            {
                return;
            }

            AudioClip clip = _footstepAudioClips[Random.Range(0, _footstepAudioClips.Length)];
            if (clip != null)
            {
                AudioSource.PlayClipAtPoint(clip, transform.position, _footstepAudioVolume);
            }
        }
    }
}