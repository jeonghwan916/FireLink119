using FireLink119.Player;
using UnityEngine;

public class SmokeArea : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerCough playerCough = other.GetComponentInParent<PlayerCough>();

            if (playerCough != null)
            {
                playerCough.StartCough();
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerCough playerCough = other.GetComponent<PlayerCough>();

            if (playerCough != null)
            {
                playerCough.StopCough();
            }
        }
    }
}