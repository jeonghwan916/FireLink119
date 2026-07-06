using FireLink119.Player;
using UnityEngine;

namespace FireLink119.Smoke
{
    [RequireComponent(typeof(Collider))]
    public class SmokeArea : MonoBehaviour
    {
        [SerializeField] private string _playerTag = "Player";

        private void Awake()
        {
            Collider areaCollider = GetComponent<Collider>();
            areaCollider.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            PlayerCough playerCough = ResolvePlayerCough(other);
            if (playerCough != null)
            {
                playerCough.StartCough();
            }
        }

        private void OnTriggerExit(Collider other)
        {
            PlayerCough playerCough = ResolvePlayerCough(other);
            if (playerCough != null)
            {
                playerCough.StopCough();
            }
        }

        private PlayerCough ResolvePlayerCough(Collider other)
        {
            if (!other.CompareTag(_playerTag))
            {
                return null;
            }

            return other.GetComponentInParent<PlayerCough>();
        }
    }
}