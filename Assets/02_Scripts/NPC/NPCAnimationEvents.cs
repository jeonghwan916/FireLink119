using UnityEngine;
using Random = UnityEngine.Random;

namespace FireLink119.NPC
{
    public class NPCAnimationEvents : MonoBehaviour
    {
        public AudioClip[] FootstepAudioClips;
        [Range(0, 1)] public float FootstepAudioVolume = 0.5f;

        private void OnFootstep(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight <= 0.5f)
            {
                return;
            }

            if (FootstepAudioClips == null || FootstepAudioClips.Length == 0)
            {
                return;
            }

            int index = Random.Range(0, FootstepAudioClips.Length);
            AudioClip clip = FootstepAudioClips[index];
            if (clip == null)
            {
                return;
            }

            AudioSource.PlayClipAtPoint(clip, transform.position, FootstepAudioVolume);
        }
    }
}