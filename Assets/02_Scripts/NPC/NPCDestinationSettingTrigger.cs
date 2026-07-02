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

        [Header("Legacy Destination")]
        [SerializeField] private Transform _doorTarget;
        [SerializeField] private Transform _destination;

        [Header("Dialogue")]
        [SerializeField] private int _dialogueId = InvalidTargetId;

        [Networked] private NetworkBool HasEntered { get; set; }

        private void OnTriggerEnter(Collider other)
        {
            Debug.Log($"[NPCDestTrigger] Enter other={other.name}, HasStateAuthority={HasStateAuthority}, HasEntered={HasEntered}");
            
            if (!HasStateAuthority || HasEntered)
            {
                Debug.Log("[NPCDestTrigger] Ignored by authority/entered guard.");
                return;
            }

            NPCController npcController = other.GetComponentInParent<NPCController>();
            Debug.Log($"[NPCDestTrigger] NPCController found={npcController != null}");
            
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
            Debug.Log($"[NPCDestTrigger] RequestDialogue id={_dialogueId}");
            
            if (_dialogueId == InvalidTargetId)
            {
                Debug.Log("[NPCDestTrigger] Dialogue skipped: InvalidTargetId");
                return;
            }

            npcController.RequestPlayDialogue(_dialogueId);
        }

        private void RequestDestination(NPCController npcController)
        {
            if (_destinationId != InvalidTargetId)
            {
                if (_doorTargetId != InvalidTargetId)
                {
                    npcController.RequestSetDestinationViaDoor(_doorTargetId, _destinationId);
                    return;
                }

                npcController.RequestSetDestination(_destinationId);
                return;
            }

            RequestLegacyDestination(npcController);
        }

        private void RequestLegacyDestination(NPCController npcController)
        {
            if (_doorTarget != null)
            {
                npcController.SetTargetViaDoor(_doorTarget, _destination);
                return;
            }

            npcController.SetTarget(_destination);
        }
    }
}