# PLAN for Extinguisher NetworkTransform Refactor

## Goal

`Extinguisher.cs`에서 소화기 본체의 position/rotation을 직접 `[Networked]` 값으로 관리하지 않는다.

소화기 본체의 spatial sync는 Fusion `NetworkTransform`에 맡기고, `Extinguisher.cs`는 grab 상태, authority 요청, 안전핀, 분사, 화재 판정 같은 gameplay state만 관리한다.

이 계획의 1차 목표는 `Bootstrap_SharedMode`로 `RoomScene_Test_FireAndExtinguisher`에 들어간 뒤 소화기를 잡고 놓았을 때 Rigidbody가 다시 중력으로 떨어지게 만드는 것이다. 2차 목표는 나중에 Fusion Network Physics addon 또는 `NetworkRigidbody` 계열로 전환하더라도 gameplay code 변경 비용을 낮추는 것이다.

## Assumptions

- 현재 프로젝트에는 `Fusion.NetworkTransform`이 존재한다.
- 현재 프로젝트에는 `NetworkRigidbody`, `NetworkRigidbody3D`, `NetworkRigidbody2D` 컴포넌트가 없다.
- Fusion release history 기준으로 Network Physics는 별도 addon 영역일 수 있으므로, 이 계획에서는 addon 도입을 하지 않는다.
- 소화기 root GameObject에는 이미 `NetworkObject`, `Rigidbody`, `XRGrabInteractable`, `Extinguisher`가 있다.
- 소화기는 Shared Mode에서 씬에 baked 된 `NetworkObject`로 spawn된다.
- 잡은 플레이어가 StateAuthority를 가져가고, 놓으면 다시 잡을 수 있어야 한다.
- 화재 진화 판정은 중복 호출을 피하기 위해 기존처럼 `Runner.IsSharedModeMasterClient` 피어에서만 수행한다.

## Non-Goals

- Fusion Network Physics addon을 추가하지 않는다.
- `NetworkRigidbody` 기반 구조를 이번 단계에서 구현하지 않는다.
- 안전핀 grab 조건과 socket 구조를 재설계하지 않는다.
- 화재 진화량, 화재 레이어, particle/audio 연출을 변경하지 않는다.
- `FusionRoomConnector`, `NetworkAvatarSpawner`, `NetworkPlayerAvatar`를 수정 대상으로 삼지 않는다.
- 소화기 외 다른 grab object의 네트워크 구조를 함께 바꾸지 않는다.

## Current Problem

현재 `Extinguisher.cs`는 다음 값을 직접 네트워크 상태로 들고 있다.

```csharp
[Networked] private Vector3 NetworkedPosition { get; set; }
[Networked] private Quaternion NetworkedRotation { get; set; }
[Networked] private Vector3 NetworkedRayOriginPosition { get; set; }
[Networked] private Quaternion NetworkedRayOriginRotation { get; set; }
```

그리고 `Render()`에서 로컬 플레이어가 들고 있지 않으면 매 프레임 transform을 네트워크 pose로 강제한다.

```csharp
if (!IsHeldByLocalPlayer)
{
    transform.SetPositionAndRotation(NetworkedPosition, NetworkedRotation);
}
```

이 구조에서는 release 직후 `IsHeldByLocalPlayer`가 false가 되면서 authority를 가진 로컬 인스턴스까지 `NetworkedPosition`으로 되돌아갈 수 있다. Rigidbody가 중력으로 떨어지려 해도 transform 보정이 물리 결과를 덮어써서 소화기가 공중에 떠 있는 현상이 생길 수 있다.

## Target Responsibility Split

### `NetworkTransform`

- 소화기 root transform의 position/rotation 동기화
- remote peer에서 소화기 pose 반영
- Shared Mode authority 변경 후 spatial state 보정

### `Rigidbody` / `XRGrabInteractable`

- grab 중 물리 이동
- release 후 gravity 낙하
- throw velocity 적용

### `Extinguisher.cs`

- `IsHeld`
- `HeldBy`
- `IsSafetyPinPulled`
- `IsFiring`
- grab authority request
- release 처리
- 안전핀 socket state
- particle/audio feedback
- Master Client의 fire raycast
- abandoned hold recovery

`Extinguisher.cs`는 소화기 본체 transform을 직접 네트워크 동기화하지 않는다.

## Step 1 - Prefab Component Setup

대상:

- `Assets/03_Prefabs/Extinguisher/Extinguisher.prefab`
- `Assets/01_Scenes/RoomScene_Test_FireAndExtinguisher.unity`

작업:

1. 소화기 root GameObject에 `NetworkTransform`을 추가한다.
2. `NetworkTransform`은 `NetworkObject`와 같은 root transform에 둔다.
3. scale 동기화가 필요 없으면 scale sync는 끈다.
4. parent 변경 동기화가 필요 없으면 parent sync는 끈다.
5. Rigidbody 기본값을 확인한다.
   - `useGravity = true`
   - `isKinematic = false`
   - interpolation은 기존 prefab 값을 우선 유지한다.
6. `XRGrabInteractable`의 movement type은 우선 기존 값을 유지한다.

검증:

- Unity Inspector에서 소화기 root에 `NetworkObject`, `NetworkTransform`, `Rigidbody`, `XRGrabInteractable`, `Extinguisher`가 함께 있는지 확인한다.
- scene instance override가 prefab 의도와 충돌하지 않는지 확인한다.

## Step 2 - Remove Manual Body Pose Sync

대상:

- `Assets/02_Scripts/Extinguisher/Extinguisher.cs`

제거 대상:

```csharp
[Networked] private Vector3 NetworkedPosition { get; set; }
[Networked] private Quaternion NetworkedRotation { get; set; }
```

제거 또는 대체 대상:

```csharp
private void WriteCurrentPose()
```

`Spawned()`, `FixedUpdateNetwork()`, `SetGrabbed()`, `SetReleased()`에서 body pose를 쓰기 위해 호출하던 `WriteCurrentPose()` 호출은 제거한다.

`Render()`의 transform 강제 세팅도 제거한다.

```csharp
if (!IsHeldByLocalPlayer)
{
    transform.SetPositionAndRotation(NetworkedPosition, NetworkedRotation);
}
```

검증:

- `Extinguisher.cs` 안에 소화기 본체 transform을 네트워크 값으로 직접 쓰는 코드가 남아 있지 않아야 한다.
- `Extinguisher.cs` 안에 `transform.SetPositionAndRotation(...)`으로 본체 pose를 계속 덮어쓰는 코드가 남아 있지 않아야 한다.

## Step 3 - Rework Ray Origin Usage

현재 raycast는 네트워크로 복제된 ray origin pose를 사용한다.

```csharp
Vector3 direction = NetworkedRayOriginRotation * Vector3.forward;
Physics.Raycast(NetworkedRayOriginPosition, direction, ...);
```

`NetworkTransform`이 본체 pose를 동기화하면 Master Client는 복제된 소화기 transform과 child `_rayOrigin` transform을 읽을 수 있다. 따라서 우선은 ray origin pose도 별도 `[Networked]` 값으로 들고 가지 않는다.

제거 후보:

```csharp
[Networked] private Vector3 NetworkedRayOriginPosition { get; set; }
[Networked] private Quaternion NetworkedRayOriginRotation { get; set; }
```

변경 방향:

```csharp
private void TryExtinguishFire()
{
    Transform rayOrigin = GetRayOrigin();
    Vector3 direction = rayOrigin.forward;

    if (!Physics.Raycast(
            rayOrigin.position,
            direction,
            out RaycastHit hit,
            _range,
            _fireLayer,
            QueryTriggerInteraction.Collide))
    {
        return;
    }

    FireObject fire = hit.collider.GetComponentInParent<FireObject>();
    if (fire != null)
    {
        fire.TakeExtinguish(Runner.DeltaTime);
    }
}
```

검증:

- Master Client가 아닌 플레이어가 소화기를 잡고 분사해도 Master Client 화면의 복제된 `_rayOrigin` 방향으로 화재 진화가 진행되는지 확인한다.
- ray가 한두 tick 늦게 따라오는 정도는 허용 가능하다.
- ray 방향 오차가 커서 진화가 불안정하면, body pose는 `NetworkTransform`에 맡기되 ray origin pose만 별도 네트워크 상태로 유지하는 fallback을 검토한다.

## Step 4 - Keep Gameplay Network State

유지할 `[Networked]` 값:

```csharp
[Networked] private NetworkBool IsHeld { get; set; }
[Networked] private PlayerRef HeldBy { get; set; }
[Networked] private NetworkBool IsSafetyPinPulled { get; set; }
[Networked] private NetworkBool IsFiring { get; set; }
```

유지할 public read properties:

```csharp
public bool NetworkIsHeld => IsNetworkReady && IsHeld;
public bool NetworkIsSafetyPinPulled => IsNetworkReady && IsSafetyPinPulled;
public bool NetworkIsFiring => IsNetworkReady && IsFiring;
public bool IsHeldByLocalPlayer => IsNetworkReady && IsHeld && HeldBy == Runner.LocalPlayer;
```

검증:

- `SafetyPinGrabCondition`이 기존처럼 `NetworkIsHeld`, `IsHeldByLocalPlayer`, `NetworkIsSafetyPinPulled`를 읽을 수 있어야 한다.
- 안전핀 제거 후 remote에서도 안전핀 visual/socket state가 맞아야 한다.
- 분사 particle/audio가 `IsFiring` 변화에 맞게 재생/정지되어야 한다.

## Step 5 - Authority Flow

기존 grab flow는 유지한다.

1. `selectEntered`
2. `_isLocallySelected = true`
3. `RequestGrabAuthority()`
4. 이미 `HasStateAuthority`면 바로 `SetGrabbed(Runner.LocalPlayer)`
5. 아니면 `Runner.RequestStateAuthority(Object.Id)`
6. `StateAuthorityChanged()`에서 pending grab이면 `SetGrabbed(Runner.LocalPlayer)`

기존 release flow도 1차 구현에서는 유지한다.

1. `selectExited`
2. `_isLocallySelected = false`
3. `_pendingGrab = false`
4. `ReleaseIfHeldByLocalPlayer()`
5. `SetReleased()`
6. `Runner.ReleaseStateAuthority(Object.Id)`

주의:

- release 직후 authority가 다른 peer로 넘어가는 과정에서 pose 보정이 튈 수 있다.
- Fusion release history에는 `NetworkTransform`의 Shared Mode authority 변경 보정 관련 수정 기록이 있으므로, 기존 수동 pose sync보다 `NetworkTransform` 쪽에 맡기는 편이 낫다.
- 만약 release 직후 튐이 크면, 놓는 순간 바로 authority를 release하지 않고 마지막 소유자가 유지하는 B안을 별도 검토한다.

검증:

- A가 잡으면 B는 잡을 수 없어야 한다.
- A가 놓은 뒤 B가 다시 잡을 수 있어야 한다.
- pending grab timeout이 기존처럼 동작해야 한다.

## Step 6 - Release Physics Stabilization

release 시 Rigidbody가 반드시 dynamic gravity 상태가 되도록 보정한다.

추가 후보:

```csharp
private Rigidbody _rigidbody;
```

`Awake()`에서 캐싱:

```csharp
_rigidbody = GetComponent<Rigidbody>();
```

`SetReleased()` 또는 release 직후 호출:

```csharp
private void EnsureReleasedPhysicsState()
{
    if (_rigidbody == null)
    {
        return;
    }

    _rigidbody.isKinematic = false;
    _rigidbody.useGravity = true;
    _rigidbody.WakeUp();
}
```

주의:

- 이 보정은 release 후 낙하 문제를 방지하기 위한 최소 방어 코드다.
- grab 중에 강제로 `isKinematic`을 바꾸는 코드는 추가하지 않는다. XRGrabInteractable의 기존 movement type 동작을 우선 존중한다.

검증:

- 소화기를 공중에서 놓으면 authority peer에서 즉시 아래로 떨어진다.
- remote peer에서도 떨어지는 pose가 따라온다.
- 놓은 뒤 바닥과 충돌한다.
- 놓은 뒤 다시 grab 가능하다.

## Step 7 - `ApplyNetworkState()` Scope Check

`ApplyNetworkState()`는 transform sync를 하지 않고 gameplay presentation만 처리해야 한다.

유지:

- safety pin socket active state
- safety pin visual state
- held by other player일 때 grab disable
- firing particle/audio feedback

주의:

- `_grabInteractable.enabled = false`가 Rigidbody 상태를 바꾸거나 selection을 강제로 해제하는지 테스트한다.
- local player가 잡고 있는 중에는 `_grabInteractable.enabled`를 건드리지 않아야 한다.

검증:

- 다른 플레이어가 들고 있는 소화기는 내 쪽에서 grab되지 않는다.
- 내가 들고 있는 중에는 grab interactable이 꺼져서 release 이벤트가 유실되지 않는다.

## Step 8 - Bootstrap Shared Mode Test

테스트 경로:

1. `Bootstrap_SharedMode` Play
2. `RoomScene_Test_FireAndExtinguisher` 로드
3. local avatar spawn 확인
4. 소화기 grab
5. 공중에서 release
6. 안전핀 pull
7. 분사
8. 화재 진화

1인 성공 기준:

- `Extinguisher.Spawned()`가 호출된다.
- 소화기를 잡으면 `HeldBy == Runner.LocalPlayer`가 된다.
- 소화기를 놓으면 `IsHeld == false`가 된다.
- 놓은 소화기가 중력으로 떨어진다.
- `Render()` transform 강제 보정이 없어도 remote sync 없이 1인 환경에서 동작한다.
- 안전핀과 분사가 기존처럼 동작한다.

2인 성공 기준:

- 방 생성자와 참가자가 같은 Shared Mode session에 들어간다.
- A가 잡으면 B는 같은 소화기를 grab할 수 없다.
- A가 움직이는 소화기 pose가 B에게 보인다.
- A가 놓으면 양쪽에서 소화기가 떨어진다.
- B가 이후 잡을 수 있다.
- Master Client가 아닌 플레이어가 분사해도 Master Client의 raycast로 화재가 줄어든다.

## Step 9 - Failure Branches

### 소화기가 여전히 공중에 멈춤

확인:

- `Extinguisher.cs`에 transform 강제 세팅이 남아 있는가?
- release 후 Rigidbody `isKinematic`이 true인가?
- release 후 Rigidbody `useGravity`가 false인가?
- `XRGrabInteractable`이 release 후 Rigidbody를 다시 kinematic으로 바꾸는가?
- `NetworkTransform`이 authority peer에서도 transform을 과하게 보정하는가?

대응:

- `EnsureReleasedPhysicsState()` 호출 위치를 `SetReleased()` 직후로 옮긴다.
- `Runner.ReleaseStateAuthority(Object.Id)`를 임시로 늦추거나 제거하고 마지막 소유자 authority 유지 방식을 테스트한다.

### remote에서만 pose가 이상함

확인:

- `NetworkTransform`이 root `NetworkObject`와 같은 transform에 있는가?
- scene instance에 `NetworkTransform`이 누락되었는가?
- `NetworkObject`의 `NetworkedBehaviours` 목록이 갱신되었는가?

대응:

- prefab에 컴포넌트를 추가한 뒤 scene instance override를 정리한다.
- Fusion weaver/import refresh가 필요한지 확인한다.

### 화재 진화 ray가 빗나감

확인:

- Master Client에서 `_rayOrigin` child transform이 remote pose를 충분히 따라오는가?
- `NetworkTransform` 보간 때문에 ray 방향이 시각보다 늦는가?
- grab 중 local-only attach 움직임이 authority/network pose로 제대로 반영되는가?

대응:

- ray origin pose만 별도 `[Networked]` 값으로 유지하는 fallback을 검토한다.
- fallback을 쓰더라도 body `NetworkedPosition/Rotation`은 되살리지 않는다.

## Future Migration to NetworkRigidbody

나중에 Fusion Network Physics addon을 도입해 `NetworkRigidbody` 계열을 사용할 경우, 이 리팩토링의 목표는 전환 비용을 낮추는 것이다.

전환 시 바뀔 부분:

- prefab에서 `NetworkTransform` 제거 또는 비활성
- `NetworkRigidbody` 또는 실제 addon의 3D Rigidbody sync 컴포넌트 추가
- physics simulation 설정 확인
- release/authority 전환 테스트

전환 시 유지할 부분:

- `IsHeld`
- `HeldBy`
- `IsSafetyPinPulled`
- `IsFiring`
- authority request/release의 기본 흐름
- safety pin state
- firing feedback
- fire raycast의 Master Client 단일 판정 원칙

중요:

- `Extinguisher.cs`가 `NetworkTransform` API에 깊게 의존하지 않도록 한다.
- `NetworkTransform.Teleport()` 같은 호출은 이번 리팩토링에 넣지 않는다.
- 위치 동기화 구현체를 교체할 수 있도록 `Extinguisher.cs`는 transform sync component를 직접 조작하지 않는다.

## Implementation Order

1. `Extinguisher.prefab` root에 `NetworkTransform` 추가
2. scene instance override 확인
3. `Extinguisher.cs`에서 body pose `[Networked]` 필드 제거
4. `WriteCurrentPose()` 제거
5. `Render()`의 transform 강제 보정 제거
6. ray origin을 실제 `_rayOrigin` transform 기준으로 변경
7. release physics 보정 추가
8. 1인 Bootstrap Shared Mode 테스트
9. 2인 Shared Mode 테스트
10. ray 오차나 authority release 튐이 있으면 fallback 분기 검토

## Success Criteria

- `Extinguisher.cs`가 소화기 본체 position/rotation을 직접 네트워크 필드로 관리하지 않는다.
- `Extinguisher.cs`가 매 Render마다 소화기 본체 transform을 강제로 세팅하지 않는다.
- 소화기 root pose는 `NetworkTransform`이 동기화한다.
- 공중에서 놓은 소화기가 authority peer에서 중력으로 떨어진다.
- remote peer에서도 소화기가 떨어지는 pose를 볼 수 있다.
- 안전핀 pull, 분사, 화재 진화가 기존 기능 수준을 유지한다.
- 나중에 `NetworkRigidbody` 계열로 전환할 때 gameplay state 코드는 대부분 유지 가능하다.

## Self Review

- 이 계획은 현재 프로젝트에 없는 `NetworkRigidbody`를 전제로 하지 않는다.
- `NetworkTransform`이 현재 프로젝트에 존재한다는 점은 `Assets/Photon/Fusion/Assemblies/Fusion.Runtime.xml`과 `Fusion.Runtime.dll.meta`에서 확인할 수 있다.
- `NetworkTransform`은 물리 전용 컴포넌트가 아니므로, release 후 Rigidbody 상태 보정과 실제 Shared Mode 테스트가 필요하다고 명시했다.
- 기존 수동 `NetworkedPosition/Rotation`과 `NetworkTransform`을 동시에 쓰지 않도록 제거 대상을 명시했다.
- ray origin pose를 제거하는 방향은 단순하지만, Master Client에서 remote ray 오차가 생길 수 있어 fallback 조건을 별도로 적었다.
- authority release 정책은 기존 흐름을 1차로 유지하되, release 직후 튐이 있으면 마지막 소유자 authority 유지 방식을 검토하도록 분기했다.
- `Extinguisher.cs`가 `NetworkTransform` API에 직접 의존하지 않는 설계를 명시해 향후 Network Physics addon 전환 비용을 줄였다.
- 테스트 기준은 `Bootstrap_SharedMode -> RoomScene_Test_FireAndExtinguisher` 경로와 1인/2인 Shared Mode를 모두 포함한다.

## Refactored Script Draft

아래 초안은 `NetworkTransform`이 소화기 root에 붙어 있다는 전제의 `Extinguisher.cs` 리팩토링안이다.

의도적으로 넣지 않는 것:

- 수동 body pose sync
- `NetworkedPosition`
- `NetworkedRotation`
- body transform에 대한 `SetPositionAndRotation`
- 디버그 로그
- 불필요한 fallback branch
- `NetworkTransform` API 직접 호출

유지하는 것:

- grab authority request
- pending grab timeout
- safety pin state
- firing state
- particle/audio feedback
- Master Client 단일 fire raycast
- release 후 Rigidbody gravity 보정

```csharp
using FireLink119.Fire;
using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FireLink119.Extinguisher
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkTransform))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(XRGrabInteractable))]
    public class Extinguisher : NetworkBehaviour, IStateAuthorityChanged
    {
        [Header("Particle")]
        [SerializeField] private ParticleSystem _smokeParticle;

        [Header("Raycast")]
        [SerializeField] private Transform _rayOrigin;
        [SerializeField] private float _range = 5f;
        [SerializeField] private LayerMask _fireLayer;

        [Header("Safety Pin")]
        [SerializeField] private XRSocketInteractor _safetyPinSocket;
        [SerializeField] private GameObject[] _safetyPinVisuals;

        [Header("Authority")]
        [SerializeField] private float _grabAuthorityRequestTimeout = 0.75f;

        [Networked] private NetworkBool IsHeld { get; set; }
        [Networked] private PlayerRef HeldBy { get; set; }
        [Networked] private NetworkBool IsSafetyPinPulled { get; set; }
        [Networked] private NetworkBool IsFiring { get; set; }

        public bool IsNetworkReady => _isSpawned && Object != null && Runner != null;
        public bool NetworkIsHeld => IsNetworkReady && IsHeld;
        public bool NetworkIsSafetyPinPulled => IsNetworkReady && IsSafetyPinPulled;
        public bool NetworkIsFiring => IsNetworkReady && IsFiring;
        public bool IsHeldByLocalPlayer => IsNetworkReady && IsHeld && HeldBy == Runner.LocalPlayer;

        private XRGrabInteractable _grabInteractable;
        private Rigidbody _rigidbody;
        private AudioSource _extinguisherSfx;
        private bool _isSpawned;
        private bool _isLocallySelected;
        private bool _pendingGrab;
        private float _pendingGrabStartedTime;
        private bool _lastRenderedFiring;
        private bool _lastRenderedSafetyPinPulled;

        private void Awake()
        {
            _grabInteractable = GetComponent<XRGrabInteractable>();
            _rigidbody = GetComponent<Rigidbody>();
            _extinguisherSfx = GetComponent<AudioSource>();

            if (_rayOrigin == null)
            {
                _rayOrigin = transform;
            }
        }

        private void OnEnable()
        {
            _grabInteractable.selectEntered.AddListener(OnGrabbed);
            _grabInteractable.selectExited.AddListener(OnReleased);
            _grabInteractable.activated.AddListener(OnFireStart);
            _grabInteractable.deactivated.AddListener(OnFireEnd);

            if (_safetyPinSocket != null)
            {
                _safetyPinSocket.selectExited.AddListener(OnSafetyPinSocketExited);
            }
        }

        private void OnDisable()
        {
            _grabInteractable.selectEntered.RemoveListener(OnGrabbed);
            _grabInteractable.selectExited.RemoveListener(OnReleased);
            _grabInteractable.activated.RemoveListener(OnFireStart);
            _grabInteractable.deactivated.RemoveListener(OnFireEnd);

            if (_safetyPinSocket != null)
            {
                _safetyPinSocket.selectExited.RemoveListener(OnSafetyPinSocketExited);
            }
        }

        public override void Spawned()
        {
            _isSpawned = true;

            if (HasStateAuthority)
            {
                IsHeld = false;
                HeldBy = PlayerRef.None;
                IsSafetyPinPulled = false;
                IsFiring = false;
                EnsureReleasedPhysicsState();
            }

            ApplyNetworkState(force: true);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            _pendingGrab = false;
            _isLocallySelected = false;
        }

        public void StateAuthorityChanged()
        {
            if (HasStateAuthority && _pendingGrab)
            {
                _pendingGrab = false;
                SetGrabbed(Runner.LocalPlayer);
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!IsNetworkReady)
            {
                return;
            }

            ClearInvalidPendingGrab();

            if (HasStateAuthority)
            {
                RecoverAbandonedHold();
            }

            if (Runner.IsSharedModeMasterClient && IsFiring)
            {
                TryExtinguishFire();
            }
        }

        public override void Render()
        {
            if (!IsNetworkReady)
            {
                return;
            }

            ApplyNetworkState(force: false);
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            _isLocallySelected = true;
            RequestGrabAuthority();
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            _isLocallySelected = false;
            _pendingGrab = false;
            ReleaseIfHeldByLocalPlayer();
        }

        private void OnSafetyPinSocketExited(SelectExitEventArgs args)
        {
            if (IsHeldByLocalPlayer && HasStateAuthority)
            {
                IsSafetyPinPulled = true;
            }
        }

        private void OnFireStart(ActivateEventArgs args)
        {
            SetFiring(true);
        }

        private void OnFireEnd(DeactivateEventArgs args)
        {
            SetFiring(false);
        }

        private void RequestGrabAuthority()
        {
            if (!IsNetworkReady || IsHeldByOtherPlayer())
            {
                return;
            }

            _pendingGrab = true;
            _pendingGrabStartedTime = Time.time;

            if (HasStateAuthority)
            {
                _pendingGrab = false;
                SetGrabbed(Runner.LocalPlayer);
                return;
            }

            Runner.RequestStateAuthority(Object.Id);
        }

        private void ClearInvalidPendingGrab()
        {
            if (!_pendingGrab)
            {
                return;
            }

            if (!_isLocallySelected ||
                IsHeldByOtherPlayer() ||
                Time.time - _pendingGrabStartedTime >= _grabAuthorityRequestTimeout)
            {
                _pendingGrab = false;
            }
        }

        private void ReleaseIfHeldByLocalPlayer()
        {
            if (!IsNetworkReady || !HasStateAuthority || HeldBy != Runner.LocalPlayer)
            {
                return;
            }

            SetReleased();
            Runner.ReleaseStateAuthority(Object.Id);
        }

        private void SetGrabbed(PlayerRef player)
        {
            if (IsHeld && HeldBy != player)
            {
                return;
            }

            IsHeld = true;
            HeldBy = player;
        }

        private void SetReleased()
        {
            IsHeld = false;
            HeldBy = PlayerRef.None;
            IsFiring = false;
            EnsureReleasedPhysicsState();
        }

        private void EnsureReleasedPhysicsState()
        {
            _rigidbody.isKinematic = false;
            _rigidbody.useGravity = true;
            _rigidbody.WakeUp();
        }

        private void SetFiring(bool firing)
        {
            if (!IsHeldByLocalPlayer || !HasStateAuthority)
            {
                return;
            }

            IsFiring = firing && IsSafetyPinPulled;
        }

        private void TryExtinguishFire()
        {
            Transform rayOrigin = GetRayOrigin();

            if (!Physics.Raycast(
                    rayOrigin.position,
                    rayOrigin.forward,
                    out RaycastHit hit,
                    _range,
                    _fireLayer,
                    QueryTriggerInteraction.Collide))
            {
                return;
            }

            FireObject fire = hit.collider.GetComponentInParent<FireObject>();
            if (fire != null)
            {
                fire.TakeExtinguish(Runner.DeltaTime);
            }
        }

        private void RecoverAbandonedHold()
        {
            if (!Runner.IsSharedModeMasterClient || !IsHeld || IsActivePlayer(HeldBy))
            {
                return;
            }

            SetReleased();
        }

        private bool IsActivePlayer(PlayerRef player)
        {
            foreach (PlayerRef activePlayer in Runner.ActivePlayers)
            {
                if (activePlayer == player)
                {
                    return true;
                }
            }

            return false;
        }

        private Transform GetRayOrigin()
        {
            return _rayOrigin != null ? _rayOrigin : transform;
        }

        private void ApplyNetworkState(bool force)
        {
            if (_safetyPinSocket != null)
            {
                _safetyPinSocket.socketActive = !IsSafetyPinPulled;
            }

            ApplySafetyPinVisuals(IsSafetyPinPulled, force);

            if (!IsHeldByLocalPlayer)
            {
                _grabInteractable.enabled = !IsHeldByOtherPlayer();
            }

            if (!force && _lastRenderedFiring == IsFiring)
            {
                return;
            }

            _lastRenderedFiring = IsFiring;
            ApplyFiringFeedback(IsFiring);
        }

        private void ApplyFiringFeedback(bool firing)
        {
            if (_smokeParticle != null)
            {
                if (firing && !_smokeParticle.isPlaying)
                {
                    _smokeParticle.Play();
                }
                else if (!firing && _smokeParticle.isPlaying)
                {
                    _smokeParticle.Stop();
                }
            }

            if (_extinguisherSfx == null)
            {
                return;
            }

            if (firing && !_extinguisherSfx.isPlaying)
            {
                _extinguisherSfx.Play();
            }
            else if (!firing && _extinguisherSfx.isPlaying)
            {
                _extinguisherSfx.Stop();
            }
        }

        private void ApplySafetyPinVisuals(bool pulled, bool force)
        {
            if (!force && _lastRenderedSafetyPinPulled == pulled)
            {
                return;
            }

            _lastRenderedSafetyPinPulled = pulled;

            foreach (GameObject visual in _safetyPinVisuals)
            {
                if (visual != null)
                {
                    visual.SetActive(!pulled);
                }
            }
        }

        private bool IsHeldByOtherPlayer()
        {
            return IsHeld && HeldBy != Runner.LocalPlayer;
        }
    }
}
```

## Related Code Review

### `SafetyPinGrabCondition.cs`

현재 의존성:

- `Extinguisher.IsNetworkReady`
- `Extinguisher.IsHeldByLocalPlayer`
- `Extinguisher.NetworkIsSafetyPinPulled`

초안은 위 public property를 유지하므로 `SafetyPinGrabCondition`은 수정하지 않아도 된다.

주의할 점:

- `ApplyNetworkState()`에서 `_safetyPinSocket.socketActive = !IsSafetyPinPulled`를 유지해야 한다.
- 안전핀이 이미 뽑힌 뒤 socket이 다시 selection하는 것을 막는 현재 조건은 유지된다.

### `SafetyPinSocketInitializer.cs`

현재 이 스크립트는 시작 직후 안전핀을 socket attach 위치로 되돌린다. 소화기 body pose sync 제거와 직접 충돌하지 않는다.

주의할 점:

- `_logDebug`는 기존 serialized option이므로 이번 리팩토링에서 건드리지 않는다.
- 안전핀 Rigidbody velocity 초기화는 안전핀에만 적용되며 소화기 본체 낙하 문제와 별개다.

### `FireObject.cs`

`FireObject.TakeExtinguish(float deltaTime)`는 다음 조건에서만 실제로 진행된다.

```csharp
if (!HasStateAuthority || IsExtinguished || deltaTime <= 0f)
{
    return;
}
```

따라서 `Extinguisher.TryExtinguishFire()`를 Master Client에서만 호출하는 현재 정책이 동작하려면, 진화 대상 `FireObject`도 Master Client가 StateAuthority를 가지고 있어야 한다.

위 전제는 씬 baked `NetworkObject`의 초기 authority가 Shared Mode Master Client에 배정된다는 현재 설계와 맞지만, 다음 경우에는 진화가 멈출 수 있다.

- fire object authority가 다른 peer로 넘어간 경우
- runtime spawned fire object의 authority가 Master Client가 아닌 경우
- Master Client가 바뀐 뒤 fire object authority가 따라오지 않은 경우

이번 초안에서는 이 문제를 해결하지 않는다. 소화기 transform 리팩토링 범위를 넘기 때문이다. 다만 2인 테스트에서 "Master Client가 아닌 플레이어가 분사해도 화재가 줄어드는지"를 반드시 확인해야 한다.

### `NetworkTransform`

초안은 `NetworkTransform` API를 직접 호출하지 않는다. 이 선택은 나중에 `NetworkRigidbody` 또는 Network Physics addon으로 바꿀 때 전환 비용을 낮추기 위한 것이다.

주의할 점:

- prefab에 `NetworkTransform`을 추가한 뒤 Fusion의 `NetworkObject.NetworkedBehaviours` 목록이 올바르게 갱신되어야 한다.
- scene instance override가 오래된 component list를 들고 있으면 `NetworkTransform`이 실제 네트워크 동작에 포함되지 않을 수 있다.
- 추가 후 Unity reimport/weaver refresh가 필요한지 확인한다.

## Draft Self Review

- `NetworkedPosition`, `NetworkedRotation`, `NetworkedRayOriginPosition`, `NetworkedRayOriginRotation`을 모두 제거했다.
- `WriteCurrentPose()`를 제거했다.
- `Render()`는 visual/gameplay feedback만 적용하고 body transform을 직접 세팅하지 않는다.
- `SafetyPinGrabCondition`이 읽는 public property를 유지했다.
- `FireObject.TakeExtinguish()`의 authority 조건 때문에 Master Client와 fire object authority가 어긋나는 위험을 문서화했다.
- release 후 낙하를 위해 Rigidbody 상태 보정은 넣었지만, grab 중 Rigidbody 상태를 강제로 바꾸는 코드는 넣지 않았다.
- `NetworkTransform.Teleport()` 같은 구현체 전용 API를 쓰지 않았다.
- 디버그 로그를 추가하지 않았다.
- serialized optional field인 particle/audio/socket/visual null 처리는 유지했다. 이는 불필요한 방어코드가 아니라 prefab 구성 차이를 허용하기 위한 기존 패턴이다.

## Safety Pin Revision

### Problem

안전핀 grab 직후 바로 grab이 풀리는 문제는 `SafetyPinGrabCondition`의 조건 변화가 원인이다.

현재 흐름:

1. 소화기를 잡은 상태에서 안전핀을 grab한다.
2. 안전핀이 socket에서 빠지며 `Extinguisher.OnSafetyPinSocketExited()`가 호출된다.
3. `IsSafetyPinPulled = true`가 된다.
4. `SafetyPinGrabCondition.CanSelectByLocalHolder()`가 `!NetworkIsSafetyPinPulled` 조건 때문에 false가 된다.
5. XRI가 현재 선택 중인 손 grab도 invalid selection으로 판단해 바로 release할 수 있다.

안전핀이 뽑힌 뒤 날뛰는 문제는 SafetyPin의 물리 설정과 hierarchy 구조가 원인이다.

현재 prefab 기준 SafetyPin은 다음 상태다.

- Extinguisher root의 child
- `Rigidbody.useGravity = true`
- `Rigidbody.isKinematic = false`
- non-trigger collider
- `XRGrabInteractable.MovementType = Instantaneous`
- `Throw On Detach = true`
- `Retain Transform Parent = true`
- socket의 starting selected interactable

이 조합은 socket, parent transform, grab movement, Rigidbody physics가 동시에 같은 물체를 움직이게 만든다. socket에 꽂힌 안전핀은 물리적으로 움직이면 안 되고, 손에 잡혀 있는 동안에도 Rigidbody가 충돌/중력으로 손 추적을 방해하면 안 된다.

### Revised Direction

1. `SafetyPinGrabCondition`은 이미 선택 중인 hand interactor를 계속 허용한다.
2. socket에 꽂혀 있거나 손에 잡힌 동안 SafetyPin Rigidbody는 kinematic + gravity off 상태로 둔다.
3. 손에서 놓인 뒤에만 dynamic + gravity on 상태로 전환한다.
4. 손으로 잡히는 순간 SafetyPin을 Extinguisher hierarchy에서 분리해 parent transform 영향과 충돌을 줄인다.
5. 디버그 로그는 추가하지 않는다.

### Inspector Recommendations

SafetyPin `XRGrabInteractable`:

- `Movement Type`: Kinematic 권장
- `Throw On Detach`: Off 권장
- `Force Gravity On Detach`: On 또는 script release 처리에 맡김
- `Retain Transform Parent`: Off 권장

SafetyPin `Rigidbody`:

- 초기 `Use Gravity`: Off 권장
- 초기 `Is Kinematic`: On 권장
- `Collision Detection`: Continuous Dynamic은 유지 가능

SafetyPin collider:

- 소화기 본체 collider와 충돌하면 layer collision을 분리한다.
- 안전핀의 물리 충돌이 gameplay에 중요하지 않으면 trigger collider도 검토할 수 있다.

## Revised SafetyPinGrabCondition.cs Draft

목표:

- 뽑기 전에는 소화기를 잡은 local holder만 안전핀을 grab할 수 있다.
- 뽑힌 뒤에는 새 grab을 막는다.
- 단, 이미 안전핀을 잡고 있는 hand interactor는 `IsSafetyPinPulled == true`가 되어도 selection을 유지한다.
- socket은 뽑히기 전까지만 selection 가능하다.

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

            return CanSelectByLocalHolder(interactor, interactable);
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

        private bool CanSelectByLocalHolder(
            IXRSelectInteractor interactor,
            IXRSelectInteractable interactable)
        {
            if (_extinguisher == null || !_extinguisher.IsNetworkReady)
            {
                return false;
            }

            if (!_extinguisher.IsHeldByLocalPlayer)
            {
                return false;
            }

            if (!_extinguisher.NetworkIsSafetyPinPulled)
            {
                return true;
            }

            foreach (IXRSelectInteractor selectingInteractor in interactable.interactorsSelecting)
            {
                if (selectingInteractor == interactor)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
```

## Revised SafetyPinSocketInitializer.cs Draft

목표:

- 시작 시 안전핀을 socket에 안정적으로 배치한다.
- socket에 꽂힌 상태와 hand grab 상태에서는 SafetyPin Rigidbody가 물리로 날뛰지 않게 한다.
- hand release 후에만 안전핀이 떨어질 수 있게 한다.
- 로그와 불필요한 방어 분기는 넣지 않는다.

```csharp
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FireLink119.Extinguisher
{
    [RequireComponent(typeof(XRSocketInteractor))]
    public class SafetyPinSocketInitializer : MonoBehaviour
    {
        [SerializeField] private XRSocketInteractor _socket;
        [SerializeField] private XRGrabInteractable _safetyPin;
        [SerializeField] private int _restoreFrameCount = 2;
        [SerializeField] private bool _detachFromExtinguisherOnHandGrab = true;

        private Rigidbody _safetyPinRigidbody;

        private void Awake()
        {
            if (_socket == null)
            {
                _socket = GetComponent<XRSocketInteractor>();
            }

            if (_safetyPin != null)
            {
                _safetyPinRigidbody = _safetyPin.GetComponent<Rigidbody>();
            }
        }

        private void OnEnable()
        {
            if (_socket != null)
            {
                _socket.selectEntered.AddListener(OnSocketEntered);
            }

            if (_safetyPin != null)
            {
                _safetyPin.selectEntered.AddListener(OnSafetyPinSelected);
                _safetyPin.selectExited.AddListener(OnSafetyPinDeselected);
            }
        }

        private void OnDisable()
        {
            if (_socket != null)
            {
                _socket.selectEntered.RemoveListener(OnSocketEntered);
            }

            if (_safetyPin != null)
            {
                _safetyPin.selectEntered.RemoveListener(OnSafetyPinSelected);
                _safetyPin.selectExited.RemoveListener(OnSafetyPinDeselected);
            }
        }

        private void Start()
        {
            StartCoroutine(RestoreSocketAfterSpawn());
        }

        private IEnumerator RestoreSocketAfterSpawn()
        {
            int frames = Mathf.Max(_restoreFrameCount, 1);
            for (int i = 0; i < frames; i++)
            {
                yield return null;
            }

            for (int i = 0; i < frames; i++)
            {
                RestoreSafetyPinToSocket();
                yield return null;
            }
        }

        private void RestoreSafetyPinToSocket()
        {
            if (_socket == null || _safetyPin == null || _socket.hasSelection)
            {
                return;
            }

            Transform attach = _socket.attachTransform != null
                ? _socket.attachTransform
                : _socket.transform;

            _safetyPin.transform.SetPositionAndRotation(attach.position, attach.rotation);
            SetSafetyPinKinematic();
            _socket.socketActive = true;
        }

        private void OnSocketEntered(SelectEnterEventArgs args)
        {
            if (args.interactableObject.transform == _safetyPin.transform)
            {
                SetSafetyPinKinematic();
            }
        }

        private void OnSafetyPinSelected(SelectEnterEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor)
            {
                SetSafetyPinKinematic();
                return;
            }

            if (_detachFromExtinguisherOnHandGrab)
            {
                _safetyPin.transform.SetParent(null, true);
            }

            SetSafetyPinKinematic();
        }

        private void OnSafetyPinDeselected(SelectExitEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor)
            {
                return;
            }

            SetSafetyPinDynamic();
        }

        private void SetSafetyPinKinematic()
        {
            if (_safetyPinRigidbody == null)
            {
                return;
            }

            _safetyPinRigidbody.linearVelocity = Vector3.zero;
            _safetyPinRigidbody.angularVelocity = Vector3.zero;
            _safetyPinRigidbody.useGravity = false;
            _safetyPinRigidbody.isKinematic = true;
        }

        private void SetSafetyPinDynamic()
        {
            if (_safetyPinRigidbody == null)
            {
                return;
            }

            _safetyPinRigidbody.isKinematic = false;
            _safetyPinRigidbody.useGravity = true;
            _safetyPinRigidbody.WakeUp();
        }
    }
}
```

## Extinguisher.cs Safety Pin Impact

`Extinguisher.cs`의 안전핀 관련 흐름은 유지한다.

```csharp
private void OnSafetyPinSocketExited(SelectExitEventArgs args)
{
    if (IsHeldByLocalPlayer && HasStateAuthority)
    {
        IsSafetyPinPulled = true;
    }
}
```

수정하지 않는 이유:

- 안전핀을 socket에서 뽑는 순간 네트워크 gameplay state가 바뀌는 위치는 여기 하나로 유지하는 것이 단순하다.
- grab 유지 문제는 `SafetyPinGrabCondition`에서 현재 selecting interactor를 허용하면 해결된다.
- 물리 날뜀 문제는 SafetyPin의 Rigidbody state 관리로 해결하는 것이 맞다.

단, 2인 테스트에서 Master Client가 아닌 플레이어가 안전핀을 뽑았는데 `IsSafetyPinPulled`가 늦게 반영되거나 누락되면, 안전핀 state authority 흐름을 별도로 점검해야 한다.

## Safety Pin Revision Self Review

- grab 직후 release되는 직접 원인인 `NetworkIsSafetyPinPulled` 필터 invalidation을 수정했다.
- 이미 잡고 있는 interactor만 예외 허용하므로, 뽑힌 뒤 새 hand grab을 무제한 허용하지 않는다.
- socket은 기존처럼 뽑히기 전까지만 선택 가능하게 유지했다.
- 안전핀 Rigidbody는 socket/hand selected 상태에서 kinematic이므로 socket과 hand tracking이 물리와 싸우지 않는다.
- hand release 후에만 dynamic gravity로 전환한다.
- debug log를 제거했다.
- `Extinguisher.cs`의 안전핀 네트워크 상태 변경 위치는 유지해 변경 범위를 좁혔다.
- SafetyPin 자체는 여전히 `NetworkObject`가 아니므로, 뽑힌 안전핀의 물리 위치 동기화는 이 계획의 범위 밖이다.
