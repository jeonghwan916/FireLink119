using UnityEngine;

public class NPCDestinationSettingTrigger : MonoBehaviour
{
    [Header("Destination")]
    // Assign this only when the NPC should visit a door point before the destination.
    [SerializeField] private Transform _doorTarget;
    [SerializeField] private Transform _destination;
    
    [Header("Flag")]
    [SerializeField] private bool _hasEntered = false;
    
    private NPCController _npcController;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("NPC") && !_hasEntered)
        {
            _hasEntered = true;
            
            _npcController = other.gameObject.GetComponent<NPCController>();

            // The controller owns the route; this trigger only supplies route data.
            if (_doorTarget != null)
            {
                _npcController.SetTargetViaDoor(_doorTarget, _destination);
            }
            else
            {
                _npcController.SetTarget(_destination);
            }
        }
    }
}
