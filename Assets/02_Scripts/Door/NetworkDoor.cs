using Fusion;
using UnityEngine;

namespace FireLink119.Network
{
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkDoor : NetworkBehaviour
    {
        [SerializeField] private int _doorId;
        [SerializeField] private GameObject _doorVisualRoot;
        [SerializeField] private Collider[] _closedStateColliders;

        [Networked, OnChangedRender(nameof(OnOpenChanged))]
        private NetworkBool IsOpen { get; set; }

        public int DoorId => _doorId;
        public bool NetworkIsOpen => Object != null && IsOpen;

        private void Awake()
        {
            if (_closedStateColliders == null || _closedStateColliders.Length == 0)
            {
                _closedStateColliders = GetComponentsInChildren<Collider>();
            }
        }

        public override void Spawned()
        {
            if (HasStateAuthority)
            {
                IsOpen = false;
            }

            ApplyOpenState();
        }

        public void Open()
        {
            if (!HasStateAuthority || IsOpen)
            {
                return;
            }

            IsOpen = true;
            ApplyOpenState();
        }

        private void OnOpenChanged()
        {
            ApplyOpenState();
        }

        private void ApplyOpenState()
        {
            if (_doorVisualRoot != null)
            {
                _doorVisualRoot.SetActive(!IsOpen);
            }

            foreach (Collider closedStateCollider in _closedStateColliders)
            {
                if (closedStateCollider != null)
                {
                    closedStateCollider.enabled = !IsOpen;
                }
            }
        }
    }
}