using UnityEngine;
using Random = UnityEngine.Random;

public class NPCAnimationEvents : MonoBehaviour
{
    private CharacterController _controller;
    public AudioClip[] FootstepAudioClips;
    [Range(0, 1)] public float FootstepAudioVolume = 0.5f;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
    }

    private void OnFootstep(AnimationEvent animationEvent)
    {
        // 발소리 처리
        if (animationEvent.animatorClipInfo.weight > 0.5f)
        {
            if (FootstepAudioClips.Length > 0)
            {
                var index = Random.Range(0, FootstepAudioClips.Length);
                AudioSource.PlayClipAtPoint(FootstepAudioClips[index], transform.TransformPoint(_controller.center), FootstepAudioVolume);
            }
        }
    }
}