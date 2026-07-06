# PLAN for Shared Mode - SafetyPinGrabCondition.cs

```csharp
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FireLink119.Extinguisher
{
    public class SafetyPinGrabCondition : MonoBehaviour, IXRSelectFilter
    {
        [SerializeField] private Extinguisher _extinguisher;
        [SerializeField] private bool _allowSocketSelectionBeforePulled = true;
        [SerializeField] private bool _blockSocketSelectionAfterPulled = true;

        public bool canProcess => isActiveAndEnabled;

        private void Awake()
        {
            if (_extinguisher == null)
            {
                _extinguisher = GetComponentInParent<Extinguisher>();
            }
        }

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (interactor is XRSocketInteractor)
            {
                return CanSelectBySocket();
            }

            return CanSelectByLocalHolder();
        }

        private bool CanSelectBySocket()
        {
            if (!_allowSocketSelectionBeforePulled)
            {
                return false;
            }

            if (_extinguisher == null || !_extinguisher.IsNetworkReady)
            {
                return true;
            }

            return !_blockSocketSelectionAfterPulled || !_extinguisher.NetworkIsSafetyPinPulled;
        }

        private bool CanSelectByLocalHolder()
        {
            if (_extinguisher == null || !_extinguisher.IsNetworkReady)
            {
                return false;
            }

            return _extinguisher.IsHeldByLocalPlayer &&
                   !_extinguisher.NetworkIsSafetyPinPulled;
        }
    }
}
```

## Self Review

- This script does not write networked state. It only filters local XR selection.
- Hand selection is allowed only when the local player is already holding the extinguisher and the pin has not been pulled.
- Socket selection remains allowed before the pin is pulled, and can be blocked after the pin is pulled.
- `InputAuthority`, Host/Client role checks, `runner.IsServer`, RPCs, and debug logs are not used.
