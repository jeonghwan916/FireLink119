# PLAN for Shared Mode - NetworkDoor.cs

```csharp
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
```

## Self Review

- 문은 Shared Mode 월드 규칙 객체이므로 `NetworkObject`를 `Is Master Client`로 설정하는 전제에서 동작한다.
- `Open()`은 `HasStateAuthority`에서만 `IsOpen`을 변경한다.
- NPC의 `NetworkDoor.Open()` 호출은 NPC와 문이 모두 Master Client 권위 객체일 때 성공한다.
- 임의 플레이어가 직접 문 상태를 바꾸는 RPC 경로를 만들지 않았다.
- 로그, 입력 권위 의존, 서버 모드 역할 분기, authority override 호출은 사용하지 않았다.
