# PLAN for Shared Mode - SmokeArea.cs

```csharp
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
```

## Self Review

- Smoke cough remains local feedback only, so this script does not use Fusion or networked state.
- Enter and exit now resolve `PlayerCough` through the same parent lookup path, preventing cough from staying active when the collider is on a child object.
- The trigger collider is enforced locally in `Awake()` without adding network authority checks.
- No debug logs, RPCs, InputAuthority checks, server role checks, or Master Client decisions are used.
