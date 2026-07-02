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
            if (npcController == null)
            {
                return;
            }

            npcController.NotifyOpeningDoorAnimationFinished();
        }
    }
}