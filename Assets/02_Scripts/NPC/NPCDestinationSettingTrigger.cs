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