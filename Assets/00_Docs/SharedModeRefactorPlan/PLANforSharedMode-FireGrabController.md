# PLAN for Shared Mode - FireGrabController.cs

## 목적

`FireGrabController.cs`를 Photon Fusion Shared Mode 기준으로 재작성하기 전에, 이 스크립트가 담당하는 역할과 플레이어 간 동기화해야 할 상태를 정리한다.

이 문서는 코드 수정 계획만 다룬다. 현재 단계에서는 `FireGrabController.cs`를 직접 수정하지 않는다.

## 현재 역할 이해

대상 파일:

- `Assets/02_Scripts/Interaction/FireGrabController.cs`

이번 계획의 전제:

- `FireGrabController`
- `Rigidbody`
- `Collider`
- `XRGrabInteractable`
- `NetworkObject`
- `NetworkTransform`

위 컴포넌트들이 모두 같은 Parent GameObject에 붙어 있다고 가정한다. 즉, 불이 꺼진 뒤 잡히는 실제 object root가 곧 네트워크 동기화 root다.

현재 동작:

1. 같은 GameObject의 `XRGrabInteractable`과 `Rigidbody`를 캐싱한다.
2. `_fireObject`가 비어 있으면 자식에서 `FireObject`를 찾는다.
3. 활성화될 때 `FireObject.OnExtinguished` 이벤트를 구독한다.
4. 활성화 직후에는 항상 `SetBurningState(true)`를 호출해 불타는 상태로 간주한다.
5. 불이 꺼졌다는 이벤트를 받으면 `SetBurningState(false)`를 호출한다.
6. 불타는 동안에는 `_disableGrabWhileBurning`이 true일 때 grab을 막고 Rigidbody를 kinematic으로 둔다.
7. 불이 꺼진 뒤에는 grab을 허용하고 Rigidbody를 dynamic으로 바꾼다.

즉, 이 스크립트의 본질은 "불이 꺼지기 전에는 물체를 못 잡게 하고, 불이 꺼진 뒤에는 잡을 수 있게 하는 로컬 interaction gate"다.

## Shared Mode에서의 기준 상태

`FireGrabController`가 직접 네트워크 truth를 가지면 안 된다.

Shared Mode에서 불이 꺼졌는지의 기준은 `FireObject`의 `[Networked] IsExtinguished` 상태다. 현재 `FireObject`는 이 상태를 `NetworkIsExtinguished` public property로 노출한다.

따라서 `FireGrabController`가 동기화해야 할 핵심 상태는 별도 `[Networked] bool CanGrab`이 아니라 다음 하나다.

- 기준 truth: `FireObject.NetworkIsExtinguished`

그리고 이 truth에서 파생되는 로컬 상태는 각 클라이언트가 동일하게 적용한다.

- `XRGrabInteractable.enabled`
- `Rigidbody.isKinematic`

단, 위 Parent에 `NetworkObject`와 `NetworkTransform`이 함께 있으므로 불이 꺼진 뒤 실제로 잡고 움직이는 위치/회전 동기화까지 고려해야 한다. 이때 위치/회전 값 자체는 `FireGrabController`가 `[Networked]` 필드로 들고 있지 않고, Parent의 `NetworkTransform`이 담당한다. `FireGrabController`는 grab 가능 상태와 grab 시점의 `StateAuthority` 획득 흐름만 책임지는 방향이 적절하다.

## 동기화해야 할 사항

### 1. 불 꺼짐 상태

동기화 필요 여부: 필요

소유 위치:

- `FireObject`

동기화 방식:

- `FireObject`의 `[Networked] IsExtinguished`를 기준으로 한다.
- `FireGrabController`는 이 값을 읽어서 로컬 grab 가능 상태를 적용한다.

이유:

- 불이 꺼졌는지 여부는 모든 플레이어에게 같은 gameplay truth여야 한다.
- 불이 꺼진 뒤 어떤 플레이어에게는 grab 가능하고 다른 플레이어에게는 불가능하면 협동 상호작용이 깨진다.

### 2. Grab 가능 여부

동기화 필요 여부: 직접 동기화하지 않음

소유 위치:

- `FireGrabController`의 로컬 파생 상태

동기화 방식:

- `FireObject.NetworkIsExtinguished`와 `_disableGrabWhileBurning`에서 매번 계산한다.
- 별도 `[Networked] CanGrab` 값은 만들지 않는다.

이유:

- grab 가능 여부는 불 꺼짐 상태에서 완전히 파생된다.
- 같은 입력에서 같은 결과가 나오는 값을 네트워크 상태로 중복 저장하면 불일치 원인이 늘어난다.

### 3. Rigidbody kinematic 여부

동기화 필요 여부: 직접 동기화하지 않음

소유 위치:

- 로컬 물리/interaction 적용 상태

동기화 방식:

- 불타는 동안 `isKinematic = true`
- 불이 꺼진 뒤 `isKinematic = false`

주의:

- 이 값은 모든 클라이언트에서 같은 시점에 비슷하게 적용되어야 한다.
- 하지만 네트워크 truth로 올릴 필요는 없고, `FireObject.NetworkIsExtinguished`에서 파생하면 충분하다.
- 물체 이동 자체는 해당 prefab에 붙은 `NetworkTransform`, grab/carry controller, 또는 별도 물체 권위 정책의 책임이다.

### 4. 물체 위치/회전

동기화 필요 여부: 필요

소유 위치:

- Parent GameObject의 `NetworkObject` / `NetworkTransform`

동기화 방식:

- Parent에 이미 붙어 있는 `NetworkTransform`이 position/rotation 동기화를 담당한다.
- `FireGrabController`는 position/rotation을 직접 `[Networked]` 필드로 만들지 않는다.
- grab을 시작한 플레이어가 Parent `NetworkObject`의 `StateAuthority`를 가져야 `NetworkTransform`이 그 플레이어의 움직임을 나머지 플레이어에게 복제할 수 있다.

주의:

- `NetworkTransform`만 붙어 있어도 현재 조작자가 `StateAuthority`가 아니면 움직임이 안정적으로 동기화되지 않을 수 있다.
- 따라서 불이 꺼진 뒤 grab 가능하게 만드는 것과 별개로, grab 시작 시 `RequestStateAuthority()` 흐름이 필요하다.
- Parent가 scene baked `NetworkObject`라면 기본 StateAuthority는 Master Client일 가능성이 높다. 실제로 잡는 플레이어가 움직임을 써야 하므로 `Allow State Authority Override` 또는 release 정책을 같이 확인해야 한다.

### 5. 누가 잡고 있는지

동기화 필요 여부: 최소한의 권위 흐름은 필요

이유:

- 현재 `FireGrabController`에는 holder 개념이 없다.
- 하지만 Parent에 `NetworkObject`와 `NetworkTransform`이 같이 있다면, 누가 움직임을 쓸 수 있는지는 `StateAuthority`로 결정된다.
- 따라서 이 스크립트가 완전한 holder gameplay state를 네트워크로 저장하지 않더라도, grab 시점의 authority request는 다룰 필요가 있다.

권장 범위:

- 단일 `XRGrabInteractable` 물체라면 `FireGrabController`에서 `selectEntered` 시 `RequestStateAuthority()`를 호출하는 구조를 검토한다.
- 권위 획득 성공 전에는 실제 이동이 remote에 복제되지 않을 수 있으므로, `StateAuthorityChanged()` 또는 pending grab 플래그로 성공 시점을 처리하는 방식이 안전하다.
- 동시 grab 충돌을 엄격히 막아야 한다면 `IsHeld`, `HeldBy` 같은 `[Networked]` 상태가 필요할 수 있다.
- `TwoHandleCarryController`처럼 두 플레이어 협동 carry가 핵심인 물체라면 holder 모델이 달라지므로, 이 스크립트에 단일 holder 상태를 넣는 대신 carry controller 쪽에서 별도 Shared Mode 설계를 해야 한다.

## 현재 구조의 문제점

### 문제 1. `OnEnable()`에서 항상 burning 상태로 초기화한다

현재 코드는 활성화될 때 무조건 다음을 실행한다.

```csharp
SetBurningState(true);
```

Shared Mode에서는 늦게 들어온 클라이언트나 오브젝트 재활성화 시점에 이미 불이 꺼져 있을 수 있다. 이때 로컬에서 일단 grab을 잠갔다가 `OnExtinguished` 이벤트를 기다리는 구조는 위험하다.

특히 `FireObject.OnExtinguished` 이벤트는 `FireObject.Render()`에서 네트워크 상태 변화에 따라 발생한다. 이미 `IsExtinguished == true`인 상태로 들어온 경우 이벤트가 다시 발생하지 않거나, 발생 타이밍이 `FireGrabController.OnEnable()`보다 늦을 수 있다.

수정 방향:

- `OnEnable()`에서 `true`를 고정으로 넣지 않는다.
- 현재 `FireObject.NetworkIsExtinguished`를 읽어 즉시 상태를 맞춘다.
- 네트워크 준비 전이면 보수적으로 grab disabled 상태를 적용한다.

### 문제 2. 이벤트만 믿으면 late join / re-enable에 약하다

`OnExtinguished` 이벤트는 좋은 알림 수단이지만, Shared Mode truth 자체는 아니다.

수정 방향:

- 이벤트는 즉시 반응용으로만 사용한다.
- `Render()` 또는 `Update()`에서 마지막으로 적용한 extinguished 상태와 `FireObject.NetworkIsExtinguished`를 비교해 보정하는 방식을 검토한다.
- 더 단순하게는 `OnEnable()`과 `HandleFireExtinguished()`에서만 `ApplyFromFireState()`를 호출하되, late join 테스트에서 누락이 있으면 `Update()` 보정을 추가한다.

추천:

- 1차 구현은 `OnEnable()`에서 현재 네트워크 상태를 즉시 적용하고 이벤트를 유지한다.
- late join에서 grab state가 틀어지면 `Update()` polling을 추가한다.

### 문제 3. FireObject 참조가 없을 때의 정책이 불명확하다

현재 `_fireObject`가 없으면 `SetBurningState(true)` 때문에 grab이 잠긴다.

Shared Mode에서도 이 보수적 기본값은 유지할 수 있다. 다만 문서와 코드에서 의도를 명확히 해야 한다.

정책:

- `_fireObject`가 없거나 네트워크 준비 전이면 불타는 상태로 간주한다.
- 즉, 안전한 기본값은 grab disabled다.

### 문제 4. Grab 허용 후 움직임 동기화 권위가 빠질 수 있다

Parent에 `NetworkTransform`이 붙어 있더라도 Shared Mode에서는 현재 움직이는 플레이어가 해당 `NetworkObject`의 `StateAuthority`를 가져야 한다.

현재 `FireGrabController`는 grab 가능 여부만 바꾸고, grab 시 authority를 요청하지 않는다. 이 상태에서는 다음 문제가 생길 수 있다.

- 로컬에서는 물체가 손을 따라 움직이지만 remote player에게는 움직임이 제대로 보이지 않는다.
- Master Client가 아닌 플레이어가 잡았을 때 transform 변경이 네트워크 truth로 기록되지 않는다.
- release 후 authority가 애매하게 남아 다음 플레이어가 잡기 어려울 수 있다.

수정 방향:

- Parent의 `NetworkObject`를 캐싱한다.
- 불이 꺼진 뒤 `selectEntered`에서 `RequestStateAuthority()`를 호출한다.
- 이미 `HasStateAuthority`이면 바로 grab 이동을 허용한다.
- 권위 요청 중에는 pending 상태를 두고, 필요하면 grab interactable을 잠깐 제한하거나 권위 획득 후 상태를 확정한다.
- `selectExited`에서는 release 후 권위를 유지할지, `ReleaseStateAuthority()`로 반납할지 정책을 정한다.

## 리팩토링 방향

### 방향 A. MonoBehaviour 유지

부분 추천안이다.

불 꺼짐에 따른 grab gate만 다룬다면 `FireGrabController`는 `NetworkBehaviour`가 될 필요가 없다. 네트워크 truth는 이미 `FireObject`가 가지고 있고, 이 스크립트는 로컬 XRI 컴포넌트를 켜고 끄는 adapter 역할만 하기 때문이다.

작업:

1. `OnEnable()`에서 이벤트를 구독한 뒤 `ApplyFromFireState()`를 호출한다.
2. `ApplyFromFireState()`는 `FireObject.NetworkIsExtinguished`를 읽어 `SetBurningState(!isExtinguished)`를 호출한다.
3. `_fireObject`가 없거나 아직 network ready가 아니면 burning으로 간주한다.
4. `HandleFireExtinguished()`는 `SetBurningState(false)` 또는 `ApplyFromFireState()`만 호출한다.
5. 같은 상태를 반복 적용해도 문제 없게 `_lastAppliedCanGrab` 같은 캐시를 둘지 검토한다. 단, 현재 작업에는 필수는 아니다.

장점:

- 코드 변경량이 작다.
- `FireGrabController`의 책임이 명확하게 유지된다.
- 별도 `[Networked]` 상태를 만들지 않아 중복 truth가 없다.

단점:

- late join이나 재활성화 타이밍이 꼬이면 이벤트만으로는 부족할 수 있다.
- 필요하면 간단한 polling 보정이 추가될 수 있다.
- grab 이후 움직임 동기화를 위해서는 별도 authority request 코드가 필요하다. 이 코드를 `MonoBehaviour`에서 `NetworkObject` 참조와 `Runner` 접근만으로 처리하기 어렵다면 `NetworkBehaviour` 전환을 검토해야 한다.

### 방향 B. NetworkBehaviour로 전환

조건부 추천안이다.

가능한 형태:

- `FireGrabController`가 `NetworkBehaviour`가 되어 Parent `NetworkObject`의 authority 흐름을 직접 다룬다.
- 불 꺼짐 여부는 여전히 `FireObject.NetworkIsExtinguished`를 읽는다.
- grab 시작 시 `Object.RequestStateAuthority()` 또는 `Runner.RequestStateAuthority(Object.Id)`를 호출한다.
- grab release 시 `Object.ReleaseStateAuthority()` 또는 `Runner.ReleaseStateAuthority(Object.Id)`를 호출할지 정책을 적용한다.

주의:

- `NetworkBehaviour`로 전환하더라도 `[Networked] CanGrab` 또는 `[Networked] IsBurning`은 만들지 않는다.
- 불 꺼짐 truth는 계속 `FireObject` 하나로 유지한다.
- 이 전환의 목적은 grab 가능 여부 동기화가 아니라, Parent `NetworkTransform`을 움직일 StateAuthority 획득 흐름을 이 스크립트에서 처리하기 위한 것이다.

결론:

- Parent에 `NetworkObject`와 `NetworkTransform`이 있고 이 오브젝트를 플레이어가 직접 들고 움직이는 요구가 확정이라면 `NetworkBehaviour` 전환을 검토할 가치가 있다.
- 그래도 네트워크 상태는 새로 만들지 않고, `FireObject` 상태와 Parent `NetworkObject.StateAuthority`만 사용한다.

## Grab 이동 동기화 계획

Parent에 `NetworkObject`, `NetworkTransform`, `Rigidbody`, `XRGrabInteractable`이 모두 붙어 있다는 전제에서는 다음 흐름이 가장 단순하다.

1. 불이 꺼지기 전:
   - `XRGrabInteractable.enabled = false`
   - `Rigidbody.isKinematic = true`
   - authority 요청 없음

2. 불이 꺼진 직후:
   - 모든 클라이언트가 `FireObject.NetworkIsExtinguished`를 보고 `XRGrabInteractable.enabled = true`로 맞춘다.
   - `Rigidbody.isKinematic = false`로 풀어 실제 grab/physics가 가능하게 한다.

3. 플레이어가 grab 시작:
   - 현재 object가 이미 local `StateAuthority`인지 확인한다.
   - 아니면 Parent `NetworkObject`에 `RequestStateAuthority()`를 요청한다.
   - 권위 획득 성공 후 local movement가 `NetworkTransform`을 통해 복제되도록 한다.

4. 플레이어가 grab 중:
   - position/rotation은 직접 `[Networked]` 필드로 쓰지 않는다.
   - Parent의 `NetworkTransform`이 authority peer의 transform을 나머지 peer에 복제한다.
   - remote peer는 같은 object를 grab하지 못하도록 `XRGrabInteractable.enabled` 또는 select filter 정책을 검토한다.

5. 플레이어가 release:
   - `Rigidbody`를 dynamic 상태로 둔다.
   - authority를 즉시 release할지, 마지막 holder가 유지할지 정한다.

권위 release 정책 후보:

- 후보 A: release 직후 `ReleaseStateAuthority()` 호출
  - 장점: 다음 플레이어가 잡기 쉽고, 월드 물체가 Master Client 쪽으로 회수되는 흐름과 맞는다.
  - 위험: release 직후 `NetworkTransform` 보정이나 물리 낙하가 튈 수 있다.

- 후보 B: release 후에도 마지막 holder가 authority 유지
  - 장점: release 직후 물리 낙하와 transform sync가 안정적일 수 있다.
  - 위험: holder가 방을 나가면 회수 처리가 필요하다.

1차 추천:

- 단순 grab object는 후보 A를 먼저 검토한다.
- release 직후 튐이나 낙하 정지가 있으면 후보 B로 바꾸고, player leave 시 Master Client 회수 로직을 별도 검토한다.

## 제안 코드 구조

불 꺼짐 상태 적용 구조는 다음 정도로 제한한다.

```csharp
private void OnEnable()
{
    if (_fireObject != null)
    {
        _fireObject.OnExtinguished += HandleFireExtinguished;
    }

    ApplyFromFireState();
}

private void HandleFireExtinguished()
{
    ApplyFromFireState();
}

private void ApplyFromFireState()
{
    bool isExtinguished = _fireObject != null && _fireObject.NetworkIsExtinguished;
    SetBurningState(!isExtinguished);
}
```

네트워크 준비 전 기본값을 더 명확히 하려면 다음처럼 분리한다.

```csharp
private bool IsFireExtinguished()
{
    return _fireObject != null && _fireObject.NetworkIsExtinguished;
}
```

단, 실제 코드 작성 시에는 현재 `FireObject.NetworkIsExtinguished => Object != null && IsExtinguished` 구조를 이용하면 된다. `Object == null`이면 false가 반환되므로 자동으로 burning으로 취급된다.

grab authority 흐름을 같은 스크립트에서 처리한다면 구조는 다음 방향을 검토한다.

```csharp
private void OnGrabbed(SelectEnterEventArgs args)
{
    if (!CanGrabFromFireState())
    {
        return;
    }

    if (Object.HasStateAuthority)
    {
        return;
    }

    Object.RequestStateAuthority();
}
```

실제 구현에서는 현재 Fusion API 사용 방식에 맞춰 `Object.RequestStateAuthority()` 또는 `Runner.RequestStateAuthority(Object.Id)` 중 프로젝트에서 쓰는 패턴을 선택한다.

## Inspector / Prefab 확인 계획

대상:

- `Assets/03_Prefabs/BurningObject/BurningTestObject.prefab`
- `Assets/03_Prefabs/BurningObject/HeavyObject.prefab`
- 관련 scene instance

확인할 것:

1. `FireGrabController._fireObject`가 올바른 `FireObject`를 가리키는지 확인한다.
2. 자동 탐색을 쓰는 경우 `GetComponentInChildren<FireObject>(true)`로 찾을 수 있는 hierarchy인지 확인한다.
3. 불이 붙은 상태에서 `XRGrabInteractable.enabled == false`인지 확인한다.
4. 불이 꺼진 상태에서 모든 클라이언트의 `XRGrabInteractable.enabled == true`인지 확인한다.
5. 불이 꺼진 뒤 Rigidbody가 dynamic이 되어 실제 grab/carry 코드가 움직일 수 있는지 확인한다.
6. Parent root에 `NetworkObject`와 `NetworkTransform`이 붙어 있는지 확인한다.
7. Parent `NetworkObject`의 `Allow State Authority Override` 설정을 확인한다.
8. 잡은 플레이어가 실제로 Parent `StateAuthority`를 획득하는지 로그 또는 Fusion inspector로 확인한다.
9. `NetworkTransform`이 Parent root transform을 동기화하고, child가 아닌 다른 transform에 붙어 있지 않은지 확인한다.

## 검증 기준

### 1인 테스트

1. `RoomScene_Test_FireAndExtinguisher` 또는 `RoomScene_Test_BurningObject`에 Shared Mode로 진입한다.
2. 불이 꺼지기 전 물체를 grab할 수 없어야 한다.
3. 소화기로 불을 꺼 `FireObject.NetworkIsExtinguished == true`가 된다.
4. 즉시 물체를 grab할 수 있어야 한다.
5. 물체가 더 이상 kinematic으로 고정되어 있지 않아야 한다.
6. grab 시 local player가 Parent `StateAuthority`를 가진다.

### 2인 테스트

1. P1과 P2가 같은 Shared Mode 세션에 들어간다.
2. 불이 꺼지기 전 P1/P2 모두 물체를 grab할 수 없어야 한다.
3. P1 또는 P2가 불을 끈다.
4. P1/P2 모두 같은 시점에 물체 grab이 가능해져야 한다.
5. 한쪽 클라이언트에서만 grab 가능하거나 한쪽에서만 kinematic이 풀리는 상태가 없어야 한다.
6. P1이 물체를 잡고 움직이면 P2에게 위치/회전이 동기화되어야 한다.
7. P2가 물체를 잡고 움직이면 P1에게 위치/회전이 동기화되어야 한다.
8. 움직임이 동기화되지 않으면 `NetworkTransform` 존재 여부보다 먼저 잡은 플레이어가 `StateAuthority`를 획득했는지 확인한다.

### Late Join 테스트

1. P1이 먼저 들어가 불을 끈다.
2. P2가 나중에 들어온다.
3. P2 입장 직후 해당 물체가 grab 가능한 상태여야 한다.
4. P2가 `OnExtinguished` 이벤트를 새로 받지 않아도 `NetworkIsExtinguished` 기반 초기 적용으로 상태가 맞아야 한다.

### Re-enable 테스트

1. 불이 꺼진 뒤 BurningObject 또는 `FireGrabController`가 비활성화/재활성화되는 상황을 만든다.
2. 재활성화 직후 `SetBurningState(true)`로 되돌아가지 않아야 한다.
3. 현재 `FireObject.NetworkIsExtinguished` 값에 맞게 grab 가능 상태가 복구되어야 한다.

## 성공 기준

- `FireGrabController`는 별도 네트워크 상태를 추가하지 않는다.
- 불 꺼짐 truth는 `FireObject.NetworkIsExtinguished` 하나로 유지한다.
- 모든 클라이언트에서 불이 꺼진 뒤 같은 grab 가능 상태가 적용된다.
- late join 클라이언트도 이벤트 재발생에 의존하지 않고 현재 네트워크 상태를 보고 grab 가능 상태를 맞춘다.
- Parent `NetworkTransform`이 잡힌 물체의 위치/회전을 나머지 플레이어에게 동기화한다.
- grab을 시작한 플레이어가 Parent `NetworkObject`의 `StateAuthority`를 획득한다.
- `FireGrabController`는 position/rotation 값을 직접 네트워크 필드로 만들지 않는다.

## 주의할 점

- `FireGrabController`를 `NetworkBehaviour`로 바꾸는 목적은 grab 가능 여부 동기화가 아니라 Parent `NetworkObject`의 authority request 처리여야 한다.
- `CanGrab` 같은 `[Networked]` 값을 새로 만들지 않는다.
- `FireObject.OnExtinguished` 이벤트는 보조 신호일 뿐, 최종 truth는 `NetworkIsExtinguished`다.
- Parent에 `NetworkTransform`이 있어도 `StateAuthority` 획득이 없으면 잡은 플레이어의 움직임이 다른 플레이어에게 동기화되지 않을 수 있다.
- `TwoHandleCarryController`처럼 두 플레이어가 동시에 조작하는 물체는 단일 holder authority 모델과 충돌할 수 있으므로 별도 계획이 필요하다.
- `Rigidbody.isKinematic`을 푸는 시점은 모든 클라이언트에서 같아야 하지만, 이 값을 직접 RPC로 맞추지 말고 `FireObject` 상태에서 파생한다.

## 구현 우선순위

1. `OnEnable()`의 초기 상태 적용을 `SetBurningState(true)`에서 `ApplyFromFireState()`로 바꾼다.
   검증: 이미 꺼진 불을 가진 object가 재활성화되어도 grab 가능 상태를 유지한다.

2. `HandleFireExtinguished()`도 `ApplyFromFireState()`를 호출하게 정리한다.
   검증: 이벤트와 직접 상태 읽기 경로가 같은 로직을 사용한다.

3. late join에서 상태 누락이 있으면 `Update()` 또는 `Render` 성격의 polling 보정을 최소한으로 추가한다.
   검증: 나중에 들어온 플레이어도 즉시 grab 가능 상태를 본다.

4. prefab/scene에서 `_fireObject` 참조와 `NetworkObject`/`NetworkTransform` 구성을 확인한다.
   검증: `FireObject.NetworkIsExtinguished`는 동기화되고, 물체 이동은 기존 네트워크 이동 컴포넌트가 담당한다.

5. grab 시작 시 Parent `NetworkObject`의 `StateAuthority`를 잡은 플레이어가 획득하도록 구현한다.
   검증: Master Client가 아닌 플레이어가 잡고 움직여도 다른 플레이어에게 위치/회전이 동기화된다.

6. release 시 authority 정책을 적용한다.
   검증: 놓은 뒤 물체가 튀거나 멈추지 않고, 다른 플레이어가 다시 잡을 수 있다.

## 최종 판단

`FireGrabController`의 Shared Mode 리팩토링은 두 계층으로 나누어야 한다.

첫 번째 계층은 불 꺼짐에 따른 grab gate다. 동기화해야 하는 truth는 "불이 꺼졌는가" 하나이며, grab 가능 여부와 Rigidbody kinematic 상태는 그 값에서 로컬로 파생한다.

두 번째 계층은 불이 꺼진 뒤 실제로 들고 움직이는 transform 동기화다. Parent에 `NetworkObject`와 `NetworkTransform`이 이미 붙어 있다면 새 position/rotation 네트워크 필드는 만들지 않는다. 대신 grab을 시작한 플레이어가 Parent `StateAuthority`를 획득해야 하고, Parent `NetworkTransform`이 그 움직임을 다른 플레이어에게 복제해야 한다.

따라서 이 가정에서는 계획을 일부 수정해 `FireGrabController`가 grab 가능 전환뿐 아니라 grab 시 StateAuthority 요청 흐름까지 포함하도록 검토하는 것이 맞다.
