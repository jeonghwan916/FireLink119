using System;
using UnityEngine;

public class NPCDeadTrigger : MonoBehaviour
{
    [SerializeField] private bool _isExplosion = true;
    private NPCController _npcController;
    private bool _hasEntered = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("NPC") && !_hasEntered)
        {
            _hasEntered = true;
            _npcController = other.GetComponent<NPCController>();
            
            if (_isExplosion)
            {
                _npcController.DieByExplosion();
            }
            else
            {
                _npcController.DieBySmoke();
            }
        }
    }
}
