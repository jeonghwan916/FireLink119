# Shared Mode 전환 및 Host Mode 잔재 제거 계획

## 목적

이 문서는 `LobbyScene_SharedMode`에서 `RoomScene_Test_FireAndExtinguisher`로 Shared Mode 입장 후 발견된 소화기, 안전핀, 화재진압 동기화 문제를 기준으로 작성한 리팩토링 계획이다.

목표는 기존 Host Mode 전제에 의존하던 로컬 처리 흐름을 제거하고, Photon Fusion Shared Mode의 `StateAuthority` 기준으로 gameplay 상태 변경 경로를 명확히 정리하는 것이다.

## 현재 확정된 상황

1. `LobbyScene_SharedMode`에서는 Shared Mode로 입장한다.
   - `FusionRoomConnector`는 씬에 존재하지만 비활성화되어 있다.
   - 실제 진입 경로는 `TestNetworkBootstrap.cs`이며 `GameMode.Shared`로 세션을 시작한다.

2. 소화기 본체 이동은 현재 양쪽 플레이어에게 정상적으로 동기화된다.
   - 소화기 root `NetworkObject`에는 `Extinguisher`와 root `NetworkTransform`만 networked behaviour로 등록되어 있다.
   - 소화기 본체의 grab, 이동, release 흐름은 Shared Mode `StateAuthority` 요청 기반으로 동작하는 상태다.

3. P2 입장에서 안전핀은 소화기 본체를 따라 움직이지 않고 처음 위치에 고정된다.

4. P2가 처음 위치에 고정된 안전핀을 뽑아도 트리거 입력으로 소화기 분사가 시작되지 않는다.

5. P1이 안전핀을 뽑아 분사 가능한 소화기를 P2가 불에 쏘더라도 화재진압 진행도가 증가하지 않는다.

## 핵심 판단

현재 문제는 소화기 본체의 `NetworkTransform` 문제가 아니라, 안전핀과 화재진압 gameplay flow에 Host Mode 시절의 로컬 처리 전제가 남아 있어서 발생한다.

Shared Mode에서는 다음 기준을 지켜야 한다.

- 공유 gameplay 상태는 반드시 `Networked` 상태 또는 명확한 authority 요청 경로로 변경한다.
- 로컬 XR Interaction 이벤트는 입력 또는 visual trigger로만 취급한다.
- 로컬에서 hierarchy, socket selection, rigidbody state를 변경하더라도 그것을 gameplay truth로 사용하지 않는다.
- `Runner.IsSharedModeMasterClient`, `HasStateAuthority`, `Object.StateAuthority`의 책임을 섞어서 사용하지 않는다.
- FireObject, NPC, 문, 트리거 같은 월드 규칙 객체는 MasterClient 또는 해당 객체의 StateAuthority가 최종 상태를 변경한다.
- 플레이어가 직접 들고 조작하는 물체는 현재 조작자가 StateAuthority를 획득한 뒤 상태를 변경한다.

## 문제 1. 안전핀 로컬 분리 처리

### 현재 코드 흐름

관련 파일:

- `Assets/02_Scripts/Extinguisher/Extinguisher.cs`
- `Assets/02_Scripts/Extinguisher/SafetyPinSocketInitializer.cs`
- `Assets/02_Scripts/Extinguisher/SafetyPinGrabCondition.cs`
- `Assets/03_Prefabs/Extinguisher/Extinguisher.prefab`

`SafetyPinSocketInitializer.cs`는 안전핀을 손으로 잡을 때 다음처럼 로컬 transform parent를 해제한다.

```csharp
_safetyPin.transform.SetParent(null, true);
```

손에서 놓을 때도 다시 parent를 해제하고, 다음 프레임에 한 번 더 parent를 해제한다.

```csharp
_safetyPin.transform.SetParent(null, true);
StartCoroutine(DetachAfterSelectionEnds());
```

안전핀은 현재 별도의 `NetworkObject` 또는 `NetworkTransform`으로 관리되지 않는다. 따라서 어느 클라이언트에서 안전핀 parent가 해제되면 그 클라이언트의 로컬 hierarchy 상태만 바뀌고, 다른 클라이언트와 일치하지 않는다.

이 구조에서는 P2에서 안전핀이 처음 위치에 고정되는 현상이 발생할 수 있다.

### 추가 위험 요소

Unity 콘솔에 다음 경고가 반복적으로 발생했다.

```text
A collider used by an Interactable object is already registered with another Interactable object.
The SafetyPin BoxCollider will remain associated with SafetyPin XRGrabInteractable...
```

현재 소화기 본체 `XRGrabInteractable`과 안전핀 `XRGrabInteractable` 모두 collider list가 비어 있다. 이 경우 XRI가 자식 collider를 자동 수집하면서 안전핀 collider가 소화기 본체 grab과 안전핀 grab 사이에서 충돌할 수 있다.

이 경고는 단순한 noise가 아니라, 안전핀 grab, socket exit, 소화기 grab 이벤트가 꼬일 수 있다는 신호다.

### 리팩토링 방향

안전핀은 별도 NetworkObject로 만들지 않는 방향을 우선한다.

이유:

- 실제 gameplay에 필요한 상태는 "안전핀이 뽑혔는가"이다.
- 핀의 물리 위치를 모든 클라이언트에 계속 동기화할 필요가 없다.
- 작은 grab object를 별도 NetworkObject로 만들면 authority 획득, parent sync, socket sync, despawn 처리가 추가되어 복잡도가 커진다.

권장 구조:

1. 안전핀이 뽑히기 전에는 모든 클라이언트에서 소화기 자식 visual로 유지한다.
2. 로컬 hand grab 또는 socket exit는 `IsSafetyPinPulled` 변경 요청으로만 사용한다.
3. `IsSafetyPinPulled`가 true가 되면 모든 클라이언트가 같은 visual 결과를 렌더링한다.
   - 가장 단순한 방식은 안전핀 visual을 비활성화하는 것이다.
   - 핀을 뽑은 뒤 떨어지는 visual이 꼭 필요하면, 네트워크 gameplay 상태와 분리된 로컬 이펙트로 처리한다.
4. `SetParent(null, true)`는 제거하거나, `IsSafetyPinPulled`가 true로 확정된 뒤 local-only visual 처리에서만 사용한다.
5. 소화기 본체 `XRGrabInteractable`에는 본체 collider만 명시적으로 등록하고, 안전핀 collider는 제외한다.

## 문제 2. 안전핀 뽑힘 상태와 분사 상태 연결

### 현재 코드 흐름

`Extinguisher.cs`에서 안전핀 뽑힘은 socket exit 이벤트에서만 처리된다.

```csharp
private void OnSafetyPinSocketExited(SelectExitEventArgs args)
{
    if (IsHeldByLocalPlayer && HasStateAuthority)
    {
        IsSafetyPinPulled = true;
    }
}
```

분사 상태는 다음 조건을 통과해야만 true가 된다.

```csharp
private void SetFiring(bool firing)
{
    if (!IsHeldByLocalPlayer || !HasStateAuthority)
    {
        return;
    }

    IsFiring = firing && IsSafetyPinPulled;
}
```

따라서 P2가 보는 안전핀 visual을 뽑았더라도, 네트워크 상태인 `IsSafetyPinPulled`가 true로 바뀌지 않으면 `IsFiring`도 true가 될 수 없다.

### 리팩토링 방향

1. 안전핀을 뽑는 입력은 소화기 StateAuthority 보유자만 gameplay 상태로 확정한다.
2. P2가 소화기를 들고 있다면 P2가 소화기 StateAuthority를 가진 뒤 `IsSafetyPinPulled`를 true로 기록해야 한다.
3. `OnSafetyPinSocketExited`는 로컬 XRI socket 상태에 의존하기보다, "현재 소화기 보유자가 안전핀 뽑기 조건을 만족했는지"를 확인하는 명시적 메서드로 정리한다.
4. `ApplyNetworkState()`는 `IsSafetyPinPulled`를 기준으로 socket active, visual active, grab 가능 여부를 모든 클라이언트에서 동일하게 렌더링한다.
5. `_safetyPinVisuals`가 비어 있으면 visual 동기화가 불가능하므로 프리팹에서 안전핀 visual 참조를 채우거나, 코드에서 안전핀 root를 명시적으로 참조하도록 변경한다.

## 문제 3. 화재진압 권한 경로 혼합

### 현재 코드 흐름

`Extinguisher.cs`는 분사 중일 때 MasterClient에서만 화재 raycast를 실행한다.

```csharp
if (Runner.IsSharedModeMasterClient && IsFiring)
{
    TryExtinguishFire();
}
```

`TryExtinguishFire()`는 raycast hit된 `FireObject`에 직접 `TakeExtinguish()`를 호출한다.

```csharp
fire.TakeExtinguish(Runner.DeltaTime);
```

하지만 `FireObject.TakeExtinguish()`는 해당 FireObject의 StateAuthority가 아니면 즉시 return한다.

```csharp
if (!HasStateAuthority || IsExtinguished || deltaTime <= 0f)
{
    return;
}
```

즉, 현재 구조에서는 다음 세 조건이 동시에 맞아야 진압이 진행된다.

1. 소화기의 `IsFiring`이 true이다.
2. SharedModeMasterClient가 `TryExtinguishFire()`를 실행한다.
3. 그 MasterClient가 hit된 FireObject의 StateAuthority이기도 하다.

Host Mode에서는 이 전제가 자연스럽게 맞았을 가능성이 높다. Shared Mode에서는 이 전제를 보장할 수 없다.

### 리팩토링 방향

화재진압 처리 방식은 둘 중 하나로 정해야 한다.

#### A안. FireObject를 MasterClient authority 객체로 고정

현재 코드 구조에 가장 가까운 방식이다.

작업:

1. FireObject가 붙은 scene `NetworkObject`를 MasterClient Object로 설정한다.
2. MasterClient 변경 시 FireObject authority도 새 MasterClient로 이전되도록 한다.
3. `Runner.IsSharedModeMasterClient`에서 raycast와 `TakeExtinguish()`를 실행한다.
4. P2가 소화기를 들고 쏠 때 MasterClient가 보는 ray origin pose가 충분히 정확한지 검증한다.

장점:

- 현재 `Runner.IsSharedModeMasterClient` 기준 흐름을 크게 바꾸지 않아도 된다.
- FireObject의 진행도, stage, extinguished 상태를 한 권한자가 일관되게 관리한다.

위험:

- MasterClient가 보는 소화기 ray origin이 실제 조작자 화면과 다르면 불이 맞지 않을 수 있다.
- root `NetworkTransform`만으로 nozzle/ray origin 방향이 충분히 동기화되는지 확인해야 한다.

#### B안. FireObject StateAuthority에게 진압 요청 RPC 전달

Shared Mode object authority를 더 명확히 따르는 방식이다.

작업:

1. 소화기 StateAuthority가 로컬에서 raycast를 수행한다.
2. hit된 FireObject의 StateAuthority에게 진압 요청을 보낸다.
3. FireObject authority가 요청을 검증하고 `TakeExtinguish()`를 실행한다.

장점:

- 실제 조작자 기준 raycast를 사용할 수 있다.
- FireObject의 최종 상태 변경은 FireObject authority가 담당한다.

위험:

- RPC source/target 설정과 요청 검증 코드가 추가된다.
- 여러 플레이어가 동시에 같은 불을 쏠 때 처리 정책이 필요하다.

### 우선 권장안

현재 코드 변경량을 줄이려면 A안을 우선 검토한다.

단, A안을 선택하더라도 MasterClient가 사용할 ray origin pose는 반드시 검증해야 한다. root `NetworkTransform`만으로 부족하면 소화기 StateAuthority가 firing 중 ray origin position/rotation을 `[Networked]` 값으로 기록하고, MasterClient가 그 값을 사용해 raycast하도록 변경한다.

## 파일별 리팩토링 계획

### `Assets/02_Scripts/Extinguisher/Extinguisher.cs`

작업:

1. `IsSafetyPinPulled`를 안전핀 gameplay truth로 유지한다.
2. 로컬 socket event에서 바로 gameplay 상태를 확정하는 흐름을 줄이고, 소화기 StateAuthority가 명시적으로 안전핀 뽑힘을 확정하도록 정리한다.
3. `ApplyNetworkState()`에서 `IsSafetyPinPulled` 기준으로 모든 클라이언트의 안전핀 visual, socket active, grab 가능 상태를 동일하게 반영한다.
4. `SetFiring()`은 유지하되, 실패 원인을 확인할 수 있도록 임시 로그를 추가한다.
5. `TryExtinguishFire()`는 선택한 화재진압 권한 모델에 맞게 수정한다.
6. A안을 선택하면 MasterClient가 사용할 ray origin pose를 네트워크 값으로 별도 기록할지 검토한다.

검증:

- P1이 핀을 뽑으면 P1과 P2 모두 `IsSafetyPinPulled=true` visual을 본다.
- P2가 핀을 뽑으면 P1과 P2 모두 `IsSafetyPinPulled=true` visual을 본다.
- 핀을 뽑지 않은 상태에서는 트리거를 눌러도 `IsFiring=false`다.
- 핀을 뽑은 상태에서 현재 holder가 트리거를 누르면 `IsFiring=true`다.

### `Assets/02_Scripts/Extinguisher/SafetyPinSocketInitializer.cs`

작업:

1. Shared Mode gameplay truth로 사용되는 `SetParent(null, true)` 흐름을 제거한다.
2. 안전핀 parent 변경은 local-only visual이 필요할 때만 제한적으로 사용한다.
3. socket 초기화는 "핀을 뽑기 전 visual 정렬"까지만 담당하게 한다.
4. 안전핀 뽑힘 후 socket 복구 코루틴이 다시 핀을 socket으로 되돌리지 않도록 `IsSafetyPinPulled` 상태를 확인한다.

검증:

- P2 화면에서 소화기를 들고 움직일 때 안전핀이 처음 위치에 남지 않는다.
- 안전핀을 뽑기 전에는 소화기 자식 visual처럼 항상 본체를 따라간다.
- 안전핀을 뽑은 뒤 visual 처리가 모든 클라이언트에서 동일하다.

### `Assets/02_Scripts/Extinguisher/SafetyPinGrabCondition.cs`

작업:

1. 안전핀 grab 가능 조건을 `Extinguisher.IsHeldByLocalPlayer`와 `NetworkIsSafetyPinPulled` 기준으로 유지한다.
2. socket selection 조건이 뽑힌 뒤 다시 true가 되지 않도록 확인한다.
3. 필요하면 "안전핀 grab"을 실제 물리 grab이 아니라 "뽑기 입력"에 가까운 interaction으로 단순화한다.

검증:

- 소화기를 들고 있지 않은 플레이어는 안전핀을 뽑을 수 없다.
- 이미 뽑힌 안전핀을 다른 플레이어가 다시 socket selection할 수 없다.

### `Assets/02_Scripts/Fire/FireObject.cs`

작업:

1. FireObject의 StateAuthority 정책을 명확히 한다.
2. A안을 선택하면 FireObject는 MasterClient authority 객체로 취급한다.
3. B안을 선택하면 외부에서 직접 `TakeExtinguish()`를 호출하지 않고, FireObject authority가 요청을 받아 처리하도록 메서드와 RPC 경로를 분리한다.
4. `TakeExtinguish()`가 authority mismatch로 return하는 상황을 임시 로그로 확인한다.

검증:

- P1이 쏘든 P2가 쏘든 같은 불의 `ExtinguishProgress`가 증가한다.
- FireObject의 `CurrentStage`와 `IsExtinguished`가 양쪽 클라이언트에 동일하게 렌더링된다.
- 불이 꺼진 뒤 collider, particle, audio 상태가 양쪽에서 동일하다.

### `Assets/03_Prefabs/Extinguisher/Extinguisher.prefab`

작업:

1. 소화기 root `NetworkObject`에는 root 이동 동기화에 필요한 behaviour만 유지한다.
2. 안전핀에 별도 `NetworkObject`와 `NetworkTransform`을 추가하지 않는다. 단, 핀 위치 자체를 네트워크로 유지해야 한다는 요구가 생기면 별도 설계 문서를 작성한다.
3. 소화기 본체 `XRGrabInteractable` collider list에 본체 collider만 명시적으로 등록한다.
4. 안전핀 `XRGrabInteractable` collider list에는 안전핀 collider만 명시적으로 등록한다.
5. `_safetyPinVisuals` 또는 이에 준하는 visual 참조를 채워서 `IsSafetyPinPulled` 렌더링이 실제로 보이게 한다.

검증:

- Unity 콘솔에서 Interactable collider 중복 등록 경고가 사라진다.
- 안전핀 collider가 소화기 본체 grab 대상에 포함되지 않는다.
- 소화기 본체 grab과 안전핀 grab 이벤트가 서로 간섭하지 않는다.

### `Assets/01_Scenes/RoomScene_Test_FireAndExtinguisher.unity`

작업:

1. FireObject가 붙은 scene `NetworkObject`의 Shared Mode 설정을 확인한다.
2. A안을 선택하면 FireObject scene object를 MasterClient Object로 설정한다.
3. FireObject의 authority가 MasterClient 변경 시 유지되는지 확인한다.

검증:

- P1이 먼저 입장하고 P2가 나중에 입장해도 FireObject authority가 의도한 객체에 있다.
- MasterClient가 바뀌는 상황에서도 FireObject가 더 이상 진행 불가능 상태가 되지 않는다.

## 리팩토링 우선순위

### 1단계. 안전핀 visual과 collider 문제 정리

목표:

- P2에서 안전핀이 처음 위치에 고정되는 문제 제거.
- XRI collider 중복 등록 경고 제거.

작업:

1. 소화기 본체와 안전핀 collider list를 명시적으로 분리한다.
2. 안전핀 로컬 parent 해제 흐름을 제거하거나 `IsSafetyPinPulled` 이후 local-only visual로 제한한다.
3. `IsSafetyPinPulled` 기준 visual 렌더링을 실제 프리팹 참조와 연결한다.

### 2단계. 안전핀 뽑힘 상태와 분사 상태 검증

목표:

- P1, P2 어느 쪽이 안전핀을 뽑아도 `IsSafetyPinPulled`가 네트워크 상태로 확정된다.
- 현재 소화기 holder가 트리거를 누르면 `IsFiring`이 정상적으로 켜진다.

작업:

1. `OnSafetyPinSocketExited` 또는 대체 메서드에서 StateAuthority 조건을 검증한다.
2. `SetFiring()` 실패 로그를 통해 `IsHeldByLocalPlayer`, `HasStateAuthority`, `IsSafetyPinPulled` 상태를 확인한다.

### 3단계. 화재진압 authority 경로 정리

목표:

- P1, P2 어느 플레이어가 소화기를 쏘더라도 화재진압 진행도가 증가한다.

작업:

1. A안 또는 B안을 선택한다.
2. A안이면 FireObject를 MasterClient authority 객체로 고정하고 ray origin pose 검증을 추가한다.
3. B안이면 FireObject authority에 진압 요청을 전달하는 RPC 경로를 작성한다.
4. `TakeExtinguish()` authority mismatch 로그를 통해 실제 호출 주체를 확인한다.

### 4단계. Host Mode 명칭과 전제 제거

목표:

- Shared Mode 코드에서 Host/Client 역할명과 Host Mode 권한 전제를 제거한다.

작업:

1. gameplay 코드에서 `Host`, `Client`, `InputAuthority`, `IsServer` 기반 분기를 재검토한다.
2. 실제로 Shared Mode에서 사용되지 않는 Host Mode 경로는 제거한다.
3. UI 문구는 필요하면 "방 만들기", "방 참가"처럼 세션 행동 기준으로 바꾼다.

## 완료 기준

다음 조건을 모두 만족하면 이 리팩토링을 완료한 것으로 본다.

1. P1이 소화기를 들고 움직이면 P2에게 본체와 안전핀 visual이 함께 움직이는 것으로 보인다.
2. P2가 소화기를 들고 움직이면 P1에게 본체와 안전핀 visual이 함께 움직이는 것으로 보인다.
3. P1이 안전핀을 뽑으면 P1과 P2 모두 같은 안전핀 상태를 본다.
4. P2가 안전핀을 뽑으면 P1과 P2 모두 같은 안전핀 상태를 본다.
5. 안전핀을 뽑지 않으면 어떤 플레이어도 분사할 수 없다.
6. 안전핀을 뽑은 뒤 현재 holder가 트리거를 누르면 양쪽에서 분사 visual/audio가 재생된다.
7. P1이 불에 쏘면 화재진압 진행도가 증가한다.
8. P2가 불에 쏘면 화재진압 진행도가 증가한다.
9. FireObject의 stage, extinguished 상태, particle, collider 상태가 양쪽에서 동일하다.
10. Unity 콘솔에 SafetyPin collider 중복 Interactable 등록 경고가 더 이상 발생하지 않는다.

## 당장 추가하면 좋은 진단 로그

리팩토링 전후 비교를 위해 다음 로그를 임시로 추가한다.

### `Extinguisher.cs`

- `OnSafetyPinSocketExited`
  - `local`
  - `object`
  - `HasStateAuthority`
  - `Object.StateAuthority`
  - `IsHeld`
  - `HeldBy`
  - `IsHeldByLocalPlayer`
  - `IsSafetyPinPulled`

- `SetFiring`
  - 요청한 `firing`
  - `HasStateAuthority`
  - `IsHeldByLocalPlayer`
  - `IsSafetyPinPulled`
  - 최종 `IsFiring`

- `TryExtinguishFire`
  - 실행 주체가 MasterClient인지
  - ray origin position/forward
  - hit 여부
  - hit collider
  - hit FireObject의 `HasStateAuthority`

### `FireObject.cs`

- `TakeExtinguish`
  - 호출된 peer의 local player
  - `HasStateAuthority`
  - `Object.StateAuthority`
  - `deltaTime`
  - 변경 전후 `ExtinguishProgress`
  - 변경 전후 `CurrentStage`

## 주의 사항

- 안전핀 문제를 해결하기 위해 SafetyPin에 `NetworkObject`와 `NetworkTransform`을 바로 추가하지 않는다.
- 먼저 "안전핀 뽑힘 상태"를 네트워크 truth로 만들고, visual은 그 truth를 렌더링하도록 단순화한다.
- 화재진압 문제는 소화기 이동 동기화와 별개다. 소화기 본체가 잘 움직인다고 해서 MasterClient raycast와 FireObject authority가 맞는 것은 아니다.
- `Destroy When State Authority Leaves`는 월드에 남아야 하는 scene object나 공용 장비에는 위험할 수 있으므로 별도 확인이 필요하다.

## Extinguisher 프리팹 Inspector 설정 변경 계획

이 섹션은 Unity Inspector에서 `Assets/03_Prefabs/Extinguisher/Extinguisher.prefab` 설정으로 직접 확인하거나 변경해야 하는 항목이다. 일부 항목은 코드 리팩토링 전 임시 대응이고, 일부 항목은 코드 리팩토링 후에도 유지해야 하는 최종 설정이다.

목표는 안전핀을 별도 네트워크 물체로 만들지 않고, 소화기 네트워크 상태인 `IsSafetyPinPulled`를 기준으로 모든 클라이언트가 같은 visual을 렌더링하게 만드는 것이다.

### 1. Root Extinguisher NetworkObject

대상:

- `Extinguisher` prefab root
- Component: `NetworkObject`

설정:

1. `Networked Behaviours`에는 root의 `Extinguisher`와 root `NetworkTransform`만 남긴다.
2. `Nested Objects`에는 SafetyPin을 추가하지 않는다.
3. SafetyPin을 root `NetworkObject`의 nested network object로 만들지 않는다.
4. 공용 월드 장비로 유지할 소화기라면 `Destroy When State Authority Leaves`는 별도 검토한다.

주의:

- SafetyPin에 `NetworkObject`를 추가하지 않는 방향이 기본 계획이다.
- SafetyPin 위치 동기화가 아니라 "핀을 뽑았는가" 상태 동기화가 목표다.

### 2. Root Extinguisher NetworkTransform

대상:

- `Extinguisher` prefab root
- Component: `NetworkTransform`

설정:

1. 소화기 본체 이동 동기화는 root `NetworkTransform`만 사용한다.
2. SafetyPin 자식에는 `NetworkTransform`을 추가하지 않는다.
3. Nozzle, Ray Origin, SafetyPinSocket 같은 자식 transform은 root transform을 따라가는 일반 child로 둔다.

검증:

- P1이 소화기를 들고 움직일 때 P2에게 root 본체가 정상적으로 따라온다.
- P2가 소화기를 들고 움직일 때 P1에게 root 본체가 정상적으로 따라온다.
- 안전핀이 뽑히기 전에는 root child visual처럼 소화기를 따라온다.

### 3. Root Extinguisher XRGrabInteractable collider list

대상:

- `Extinguisher` prefab root
- Component: `XRGrabInteractable`

현재 위험:

- root `XRGrabInteractable`의 `Colliders` list가 비어 있으면 XRI가 자식 collider까지 자동 수집할 수 있다.
- 이때 SafetyPin의 `BoxCollider`가 root Extinguisher grab 대상과 SafetyPin grab 대상에 동시에 걸려 collider 중복 등록 경고가 발생한다.

변경:

1. root `XRGrabInteractable > Colliders` list를 수동으로 채운다.
2. 이 list에는 소화기 본체를 잡기 위한 collider만 넣는다.
3. SafetyPin의 `BoxCollider`는 root Extinguisher `Colliders` list에 넣지 않는다.
4. Handle, Body 등 본체 grab에 필요한 collider가 없다면 별도 collider를 root 또는 본체 child에 추가하고 그 collider만 등록한다.

검증:

- Unity Console에서 다음 경고가 더 이상 나오지 않아야 한다.

```text
A collider used by an Interactable object is already registered with another Interactable object.
```

### 4. SafetyPin XRGrabInteractable collider list

대상:

- `Extinguisher/SafetyPin`
- Component: `XRGrabInteractable`

변경:

1. SafetyPin `XRGrabInteractable > Colliders` list를 수동으로 채운다.
2. 이 list에는 SafetyPin 자신의 `BoxCollider`만 넣는다.
3. SafetyPin collider가 root Extinguisher grab collider로도 등록되지 않았는지 확인한다.

검증:

- 소화기 본체를 잡을 때 SafetyPin grab 이벤트가 같이 발생하지 않는다.
- SafetyPin을 잡을 때 root Extinguisher grab 이벤트가 collider 충돌 때문에 꼬이지 않는다.

### 5. SafetyPin Transform parent

대상:

- `Extinguisher/SafetyPin`

설정:

1. 초기 prefab 상태에서 SafetyPin은 root `Extinguisher`의 child로 둔다.
2. 안전핀을 뽑기 전에는 런타임에서도 SafetyPin이 root Extinguisher를 따라가야 한다.
3. 리팩토링 후 `SafetyPinSocketInitializer`가 임의로 `SetParent(null, true)`를 호출하지 않도록 코드와 함께 확인한다.

주의:

- P2 화면에서 SafetyPin이 처음 위치에 고정되는 문제는 SafetyPin이 로컬에서 parent 해제된 뒤 네트워크로 복구되지 않는 흐름과 연결된다.
- Inspector 설정만으로는 완전히 해결되지 않고, `SafetyPinSocketInitializer.cs`의 parent 해제 로직 제거가 같이 필요하다.

### 6. SafetyPin Rigidbody

대상:

- `Extinguisher/SafetyPin`
- Component: `Rigidbody`

권장 설정:

1. 안전핀을 뽑기 전 visual로만 사용할 경우 `Is Kinematic`을 켠다.
2. `Use Gravity`는 끈다.
3. 안전핀을 실제로 던지거나 물리 시뮬레이션하지 않는다면 dynamic rigidbody로 전환하지 않는다.

주의:

- 안전핀을 local-only 떨어지는 visual로 연출할 경우에만 뽑힌 뒤 local rigidbody를 dynamic으로 바꾸는 방식을 검토한다.
- 이 연출은 gameplay truth가 아니어야 하며, `IsSafetyPinPulled`의 네트워크 상태와 분리해서 생각한다.

### 7. SafetyPinSocket XRSocketInteractor

대상:

- `Extinguisher/SafetyPinSocket`
- Component: `XRSocketInteractor`

설정:

1. `Starting Selected Interactable`은 SafetyPin을 가리켜도 된다.
2. `Parent Interactable Object`는 root Extinguisher의 `XRGrabInteractable`을 가리키는 현재 구조를 유지할 수 있다.
3. `socketActive`는 런타임에서 `IsSafetyPinPulled`에 따라 제어되도록 한다.

리팩토링 후 기대:

- `IsSafetyPinPulled == false`이면 socket은 안전핀 visual을 보관하는 역할만 한다.
- `IsSafetyPinPulled == true`이면 socket은 비활성화되어 다시 SafetyPin을 선택하지 않는다.

### 8. SafetyPinSocketInitializer 설정

대상:

- `Extinguisher/SafetyPinSocket`
- Component: `SafetyPinSocketInitializer`

현재 위험 설정:

- `_detachFromExtinguisherOnHandGrab`가 true이면 손으로 잡는 순간 SafetyPin이 local world object로 분리된다.

변경:

1. 리팩토링 전 임시 대응으로는 `_detachFromExtinguisherOnHandGrab`를 false로 바꾼다.
2. 코드 리팩토링 후에는 이 필드 자체를 제거하거나, 뽑힌 뒤 local-only visual 연출에만 사용하도록 의미를 바꾼다.
3. `RestoreSafetyPinToSocket`이 이미 뽑힌 핀을 다시 socket 위치로 되돌리지 않도록 `IsSafetyPinPulled` 상태와 연결한다.

검증:

- P2가 소화기를 들고 움직일 때 SafetyPin이 원래 위치에 남지 않는다.
- SafetyPin을 잡으려는 동작이 소화기 본체의 네트워크 이동과 hierarchy를 깨지 않는다.

### 9. Extinguisher component Safety Pin Visuals

대상:

- `Extinguisher` prefab root
- Component: `Extinguisher`
- Field: `Safety Pin Visuals`

현재 위험:

- `_safetyPinVisuals`가 비어 있으면 `IsSafetyPinPulled`가 true가 되어도 visual 변화가 없다.

변경:

1. `Safety Pin Visuals` 배열에는 비활성화할 안전핀 mesh/renderer visual object만 등록한다.
2. 가능하면 `SafetyPin` root가 아니라 `SafetyPin` 아래의 mesh visual child를 등록한다.
3. 현재 구조처럼 `SafetyPin` root에 `XRGrabInteractable`, `BoxCollider`, `Rigidbody`, `SafetyPinGrabCondition`이 함께 붙어 있다면 root를 visual 대상으로 등록하지 않는다.
4. mesh visual child가 없다면 Inspector에서 별도 visual child를 만들거나, 코드에서 renderer 참조를 분리하는 방식으로 정리한다.

권장:

- SafetyPin root는 socket, grab filter, collider 제어의 기준 object로 유지한다.
- 핀이 뽑힌 뒤 더 이상 grab되지 않게 하는 처리는 root GameObject 비활성화가 아니라 `IsSafetyPinPulled` 기준의 socket/interactable/collider 제어로 처리한다.
- visual 비활성화는 mesh/renderer만 대상으로 한다.

검증:

- P1이 핀을 뽑으면 P1과 P2 모두 같은 SafetyPin visual 상태를 본다.
- P2가 핀을 뽑으면 P1과 P2 모두 같은 SafetyPin visual 상태를 본다.

### 10. SafetyPin에 추가하지 말아야 할 컴포넌트

기본 계획에서는 SafetyPin에 다음 컴포넌트를 추가하지 않는다.

- `NetworkObject`
- `NetworkTransform`

추가하지 않는 이유:

- SafetyPin은 독립적인 네트워크 소유권을 가질 gameplay object가 아니다.
- 필요한 네트워크 truth는 `Extinguisher.IsSafetyPinPulled` 하나다.
- SafetyPin을 별도 네트워크 오브젝트로 만들면 authority transfer, nested object 등록, parent sync, grab sync, despawn 처리가 모두 추가된다.

예외:

- "뽑힌 안전핀이 모든 클라이언트에서 같은 위치로 날아가고 바닥에 떨어져야 한다"는 요구가 생기면 별도 NetworkObject 설계를 다시 한다.

### Inspector 변경 후 체크리스트

1. Root Extinguisher `NetworkObject`의 `Networked Behaviours`는 `Extinguisher`, root `NetworkTransform`만 포함한다.
2. SafetyPin에는 `NetworkObject`가 없다.
3. SafetyPin에는 `NetworkTransform`이 없다.
4. Root Extinguisher `XRGrabInteractable > Colliders`에는 SafetyPin collider가 없다.
5. SafetyPin `XRGrabInteractable > Colliders`에는 SafetyPin collider만 있다.
6. `SafetyPinSocketInitializer._detachFromExtinguisherOnHandGrab`는 false로 바꾸거나 코드 리팩토링으로 제거한다.
7. `Extinguisher.Safety Pin Visuals`에는 SafetyPin root가 아니라 실제 안전핀 mesh/renderer visual 참조가 들어 있다.
8. Play Mode에서 SafetyPin collider 중복 Interactable 등록 경고가 발생하지 않는다.
9. P2 화면에서 소화기를 움직일 때 SafetyPin이 최초 위치에 남지 않는다.
10. 핀을 뽑은 뒤 양쪽 클라이언트의 SafetyPin visual 상태가 동일하다.
