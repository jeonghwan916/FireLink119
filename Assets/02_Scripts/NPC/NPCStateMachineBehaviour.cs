using UnityEngine;

public class NPCStateMachineBehaviour : StateMachineBehaviour
{
    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        // Only a naturally completed animation should advance the NPC route.
        if (stateInfo.normalizedTime < 1f)
        {
            return;
        }

        if (animator.TryGetComponent(out NPCController npcController))
        {
            npcController.FinishOpeningDoor();
        }
    }
}
