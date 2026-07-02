using Fusion;
using UnityEngine;

namespace FireLink119.Network
{
    public class NetworkDoor : NetworkBehaviour
    {
        [SerializeField] private int _doorId;
        [SerializeField] private GameObject _doorVisualRoot;

        [Networked, OnChangedRender(nameof(OnOpenChanged))]
        private NetworkBool IsOpen { get; set; }

        public int DoorId => _doorId;

        public override void Spawned()
        {
            ApplyOpenState();
        }

        public void Open()
        {
            Debug.Log($"[NetworkDoor] Open requested. DoorId={_doorId}, HasStateAuthority={HasStateAuthority}, Object={name}");
            
            if (!HasStateAuthority)
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
            GameObject target = _doorVisualRoot != null ? _doorVisualRoot : gameObject;
            target.SetActive(!IsOpen);
        }
    }
}