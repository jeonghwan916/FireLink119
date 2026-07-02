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