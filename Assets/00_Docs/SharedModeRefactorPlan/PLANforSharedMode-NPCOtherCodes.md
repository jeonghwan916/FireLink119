// NPCDestinationSettingTrigger.cs
using Fusion;
using UnityEngine;

namespace FireLink119.NPC
{
    [RequireComponent(typeof(NetworkObject))]
    public class NPCDestinationSettingTrigger : NetworkBehaviour
    {
        private const int InvalidTargetId = -1;

        [Header("Destination")]
        [SerializeField] private int _doorTargetId = InvalidTargetId;
        [SerializeField] private int _destinationId = InvalidTargetId;

        [Header("Dialogue")]
        [SerializeField] private int _dialogueId = InvalidTargetId;

        [Networked] private NetworkBool HasEntered { get; set; }

        private void OnTriggerEnter(Collider other)
        {
            if (!HasStateAuthority || HasEntered)
            {
                return;
            }

            NPCController npcController = other.GetComponentInParent<NPCController>();
            if (npcController == null)
            {
                return;
            }

            HasEntered = true;
            RequestDialogue(npcController);
            RequestDestination(npcController);
        }

        private void RequestDialogue(NPCController npcController)
        {
            if (_dialogueId != InvalidTargetId)
            {
                npcController.RequestPlayDialogue(_dialogueId);
            }
        }

        private void RequestDestination(NPCController npcController)
        {
            if (_destinationId == InvalidTargetId)
            {
                return;
            }

            if (_doorTargetId != InvalidTargetId)
            {
                npcController.RequestSetDestinationViaDoor(_doorTargetId, _destinationId);
                return;
            }

            npcController.RequestSetDestination(_destinationId);
        }
    }
}

// NPCDeadTrigger.cs
using Fusion;
using UnityEngine;

namespace FireLink119.NPC
{
    [RequireComponent(typeof(NetworkObject))]
    public class NPCDeadTrigger : NetworkBehaviour
    {
        [SerializeField] private NPCDeathType _deathType = NPCDeathType.Explosion;

        [Networked] private NetworkBool HasEntered { get; set; }

        private void OnTriggerEnter(Collider other)
        {
            if (!HasStateAuthority || HasEntered)
            {
                return;
            }

            NPCController npcController = other.GetComponentInParent<NPCController>();
            if (npcController == null)
            {
                return;
            }

            HasEntered = true;
            npcController.RequestDie(_deathType);
        }
    }
}

// NPCAnimationEvents.cs
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

// NPCStateMachineBehaviour.cs
using UnityEngine;

namespace FireLink119.NPC
{
    public class NPCStateMachineBehaviour : StateMachineBehaviour
    {
        public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.normalizedTime < 1f)
            {
                return;
            }

            NPCController npcController = animator.GetComponentInParent<NPCController>();
            if (npcController != null && npcController.HasStateAuthority)
            {
                npcController.NotifyOpeningDoorAnimationFinished();
            }
        }
    }
}
