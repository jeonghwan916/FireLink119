# Photon Fusion Shared Mode 재작성 계획

## 전제와 성공 기준

이 문서는 `PLANforHostMode.md`와 현재 `Assets/02_Scripts` 구현을 기준으로, Host Mode 전제로 재작성된 네트워크 스크립트들을 Shared Mode 기준으로 다시 설계하기 위한 계획이다. 이 단계에서는 코드를 수정하지 않고 방향, 우선순위, 위험 요소를 정리한다.

성공 기준:

1. `FusionRoomConnector`가 Host/Client가 아니라 Shared Mode 세션 생성/참가 흐름으로 동작한다.
2. `InputAuthority`, `runner.IsServer`, Host/Client 역할명에 기대는 게임플레이 코드가 제거되거나 Shared Mode에 맞는 대체 기준으로 바뀐다.
3. 모든 공유 월드 상태는 명확한 `StateAuthority` 소유 정책을 가진다.
4. 플레이어가 직접 조작하는 물체는 필요한 경우 해당 플레이어가 `StateAuthority`를 획득하고, NPC/화재/문/트리거처럼 월드 규칙을 확정하는 물체는 Master Client 권위로 고정한다.
5. 로컬 XR Origin은 계속 입력/카메라/햅틱/로컬 피드백 소스로만 사용하고, 공유 판정은 `NetworkObject`, `PlayerRef`, 네트워크 아바타 기준으로 처리한다.

## 공식 Shared Mode 제약 요약

Photon Fusion 2 문서 기준으로 Shared Mode에서 중요한 차이는 다음과 같다.

- `Runner.Spawn()`은 Host Mode에서는 Server만 호출하지만, Shared Mode에서는 해당 오브젝트의 `StateAuthority`가 되려는 클라이언트가 호출할 수 있다.
- Shared Mode의 `NetworkObject.StateAuthority`는 항상 유효한 `PlayerRef`이며, Host/Dedicated/Single처럼 `PlayerRef.None` 서버 권위 모델이 아니다.
- `StateAuthority`는 다른 플레이어에게 직접 할당할 수 없다. 필요한 플레이어가 `Object.RequestStateAuthority()`로 획득을 요청해야 한다.
- `RequestStateAuthority()`는 `Allow State Authority Override`가 켜져 있거나 기존 권위자가 `Object.ReleaseStateAuthority()`를 호출한 경우에만 성공한다.
- `InputAuthority`는 Server Mode 전용 개념이며 Shared Mode에는 적용되지 않는다. 따라서 `HasInputAuthority`, `RpcSources.InputAuthority`, `Object.InputAuthority`, `GetInput()` 기반 아바타 갱신은 재설계 대상이다.
- Shared Mode에서 씬 관리는 `SharedModeMasterClient`만 수행할 수 있다.
- 씬에 baked 된 `NetworkObject`의 초기 `StateAuthority`는 `SharedModeMasterClient`가 가진다.
- Shared Mode Settings의 `Is Master Client`가 켜진 `NetworkObject`는 Master Client 변경 시에도 권위가 새 Master Client로 이전된다.

참고:

- Photon Fusion Network Object: https://doc.photonengine.com/fusion/current/manual/network-object
- Photon Fusion Shared Mode Master Client: https://doc.photonengine.com/fusion/current/manual/shared-mode-master-client

## Host Mode 리팩토링의 핵심 의도

`PLANforHostMode.md`의 방향은 크게 세 가지였다.

1. 로컬 `MonoBehaviour`에 흩어진 월드 상태를 `[Networked]` 상태로 올린다.
2. 클라이언트 입력은 요청으로 보내고, 최종 게임 상태는 Host/StateAuthority가 검증해 기록한다.
3. 시각/청각/햅틱 같은 개인 피드백은 네트워크로 직접 복제하지 않고, 확정된 네트워크 상태를 보고 각 클라이언트가 로컬 재생한다.

이 의도 자체는 Shared Mode에서도 유효하다. 다만 "Host/StateAuthority"를 같은 의미로 쓰면 안 된다. Shared Mode에서는 오브젝트마다 권위자가 다르고, 씬 오브젝트의 기본 권위자는 Master Client다. 따라서 재작성의 핵심은 네트워크화 자체가 아니라 권위 분배 정책을 다시 세우는 것이다.

## Shared Mode 전체 설계 방향

### 1. 권위 모델을 두 계층으로 나눈다

월드 규칙 권위:

- 대상: NPC, 화재, 목적지/사망 트리거, 일반 문, 게임 시작/씬 전환, 점수/구조/사망 같은 결과 상태
- 권위자: `SharedModeMasterClient`
- 설정: 가능한 씬 오브젝트는 `Is Master Client`를 켜서 Master Client가 바뀌어도 권위가 따라가게 한다.
- 이유: 아무 플레이어나 월드 규칙 권위를 획득하게 하면 NPC 이동, 화재 진화, 문 열림, 트리거 1회성 처리 결과가 충돌할 수 있다.

조작 물체 권위:

- 대상: 플레이어 아바타, 손에 든 소화기, 플레이어가 직접 밀고 있는 물체
- 권위자: 실제 조작 중인 로컬 플레이어
- 설정: `Allow State Authority Override`를 켜고, 잡을 때 `RequestStateAuthority()`, 놓을 때 필요하면 `ReleaseStateAuthority()` 또는 Master Client 회수 정책을 적용한다.
- 이유: VR 손 추적/소화기 pose는 입력자가 직접 권위를 잡는 편이 지연과 RPC 왕복이 적다.

### 2. Host/Client 역할명을 게임플레이 권한으로 쓰지 않는다

현재 로비 UI와 `LobbyRoomRole.Host/Client`는 방 생성자와 참가자 구분에 묶여 있다. Shared Mode에서는 둘 다 같은 토폴로지의 피어이고, 방 생성자는 자동으로 초기 Master Client일 뿐이다.

수정 방향:

- UI 문구는 당장 유지할 수 있어도 내부 네트워크 의미는 `CreateRoom` / `JoinRoom` 또는 `RoomOwner` / `Participant` 쪽으로 분리한다.
- `FusionRoomConnector`는 `GameMode.Shared`를 사용한다.
- `EnableClientSessionCreation = false`를 유지하면 참가자가 없는 방을 만들지 못할 수 있으므로, 방 생성 버튼과 참가 버튼에서 세션 생성 허용 여부를 분리한다.
- 게임플레이에서 `LobbyRoomRole.Host`를 권위 판정으로 쓰지 않는다.

### 3. `InputAuthority` 기반 아바타 입력 구조를 Shared Mode 아바타 소유 구조로 바꾼다

현재 `NetworkPlayerAvatar`는 Host Mode 기준으로 `HasInputAuthority`, `RpcSources.InputAuthority`, `GetInput(out VrAvatarNetworkInput)`를 사용한다. Shared Mode에서는 이 경로가 맞지 않는다.

권장 방향:

- 각 플레이어가 자기 아바타를 직접 `Runner.Spawn()`하고 그 아바타의 `StateAuthority`가 된다.
- `NetworkAvatarSpawner`의 `runner.IsServer` 분기와 `inputAuthority: player` 전달은 제거 대상이다.
- `NetworkAvatarInputProvider`와 `VrAvatarNetworkInput` 기반 tick input 대신, 아바타의 `StateAuthority` 클라이언트가 로컬 XR Origin 값을 읽어 `[Networked]` pose/hand/animation 상태에 직접 기록한다.
- `NetworkPlayerAvatar`의 자기 자신 숨김 기준은 `HasInputAuthority`가 아니라 `Object.HasStateAuthority` 또는 `Object.StateAuthority == Runner.LocalPlayer`로 바꾼다.
- `Runner.SetPlayerObject(Runner.LocalPlayer, avatarObject)`는 각 클라이언트가 자기 스폰 직후 자기 플레이어에 대해 호출하는 방식으로 검토한다. 다른 플레이어의 player object 조회가 모든 피어에서 안정적으로 동작하는지 반드시 플레이 모드에서 확인해야 한다.

주의:

- 씬 전환 후 각 클라이언트가 자기 아바타를 중복 스폰하지 않도록 `NetworkAvatarSpawner`에 로컬 플레이어 단위 중복 방지가 필요하다.
- Shared Mode에서 플레이어가 나가면 해당 플레이어가 권위자인 스폰 오브젝트의 파괴 정책을 명확히 해야 한다. 아바타는 파괴되어도 되지만 소화기 같은 월드 물체는 파괴되면 안 된다.

## 파일별 재작성 계획

### `Assets/02_Scripts/Network/FusionRoomConnector.cs`

현재 문제:

- `LobbyRoomRole.Host`면 `GameMode.Host`, 아니면 `GameMode.Client`로 시작한다.
- Host Mode 전용으로 `NetworkAvatarInputProvider`와 `NetworkAvatarSpawner`가 동작한다.
- `EnableClientSessionCreation = false`가 Client 경로에는 맞지만 Shared Mode 생성/참가 분리에는 더 명확한 구조가 필요하다.

Shared Mode 방향:

- `StartRoom` 내부 `GameMode`는 `GameMode.Shared`로 통일한다.
- 방 생성 버튼은 세션 생성 허용, 참가 버튼은 기존 세션 참가만 허용하도록 인자를 분리한다.
- `NetworkAvatarInputProvider`는 제거하거나 Shared Mode에서 더 이상 `OnInput`을 쓰지 않는다면 비활성화한다.
- `NetworkAvatarSpawner`는 로컬 플레이어 아바타 스폰 담당으로 역할을 바꾼다.
- 씬 로드는 Master Client만 호출하도록 보장한다.

검증:

- 두 클라이언트가 같은 4자리 코드로 Shared Mode 방에 들어간다.
- 방 생성자가 먼저 들어오면 Master Client가 되고, 참가자는 세션을 새로 만들지 않는다.
- 씬 전환이 Master Client에서만 호출된다.

### `Assets/02_Scripts/Network/NetworkAvatarSpawner.cs`

현재 문제:

- `runner.IsServer`인 피어만 모든 플레이어의 아바타를 스폰한다.
- `Runner.Spawn(... inputAuthority: player)`는 Shared Mode의 핵심 소유 모델과 맞지 않는다.

Shared Mode 방향:

- 각 클라이언트가 자기 로컬 플레이어 아바타만 스폰한다.
- 스폰된 아바타는 스폰 호출자가 `StateAuthority`를 가진다.
- `inputAuthority` 인자는 제거하거나 Shared Mode에서 의미 없는 값으로 남기지 않는다.
- `OnPlayerJoined`에서 모든 플레이어를 스폰하지 말고, 로컬 플레이어가 씬 로드 완료 후 자기 아바타가 없을 때만 스폰한다.
- `OnSceneLoadStart/Done`에서는 로컬 캐시만 정리하고, 자기 아바타 재스폰 여부만 판단한다.

검증:

- 각 클라이언트가 자기 아바타 하나만 스폰한다.
- 상대 아바타가 중복 생성되지 않는다.
- `Runner.GetPlayerObject(player)`로 NPC가 따라갈 대상 아바타를 찾을 수 있다.

### `Assets/02_Scripts/Network/NetworkPlayerAvatar.cs`

현재 문제:

- `HasInputAuthority`, `Object.InputAuthority`, `RpcSources.InputAuthority`, `GetInput()`이 핵심 경로다.
- `GetExpectedRoleForInputAuthority()`는 Host/Client 버튼 의미를 `InputAuthority`로 추론하는데 Shared Mode에서는 성립하지 않는다.
- 비상문 조작도 아바타의 InputAuthority RPC를 통해 StateAuthority로 전달된다.

Shared Mode 방향:

- 로컬 소유 판정은 `HasStateAuthority` 또는 `Object.StateAuthority == Runner.LocalPlayer`로 통일한다.
- `FixedUpdateNetwork()`에서 `GetInput()`을 읽지 말고, `HasStateAuthority`인 아바타가 로컬 XR Origin 상태를 직접 찾아 `[Networked]` pose/hand/animation 상태를 쓴다.
- RPC source는 `RpcSources.StateAuthority` 또는 `RpcSources.All` 중 용도별로 다시 정한다. 소유자만 보낼 수 있어야 하는 요청은 `StateAuthority` source가 적합하다.
- 게임 시작 승인 로직은 플레이어 아바타 static 상태에 숨기기보다 별도 `NetworkRoomReadyState` 같은 Master Client 권위 씬 오브젝트로 분리하는 것이 낫다.
- 비상문 조작은 아바타를 중계자로 쓰지 말고 `NetworkEmergencyDoor` 자체를 `NetworkBehaviour`로 만들거나 별도 문 권위 객체에 요청한다.

검증:

- 자기 네트워크 아바타는 로컬에서 숨겨지고, 상대 아바타만 보인다.
- 아바타 pose/hand/animation이 `InputAuthority` 없이 동기화된다.
- 두 플레이어가 동시에 준비 버튼을 눌렀을 때 Master Client만 씬 로드를 호출한다.

### `Assets/02_Scripts/Fire/FireObject.cs`

현재 상태:

- 이미 `NetworkBehaviour`이며 진화 진행도, 단계, 완료 여부가 `[Networked]`다.
- `TakeExtinguish()`는 `HasStateAuthority`에서만 진행된다.

Shared Mode 방향:

- 화재는 월드 규칙이므로 `Is Master Client` 씬 오브젝트로 두는 편이 안전하다.
- `Allow State Authority Override`는 끄는 것을 기본으로 한다.
- 소화기가 플레이어 권위로 분사하더라도 화재 진행도는 화재의 `StateAuthority`, 즉 Master Client만 쓴다.
- 플레이어 권위 소화기가 직접 `fire.TakeExtinguish()`를 호출하면 자기 피어에서만 성공하거나 실패할 수 있으므로, Master Client에 진화 요청을 보내는 경로가 필요하다.

권장 구조:

- 단순 구현: 소화기 StateAuthority가 `RPC_RequestExtinguishFire(fireNetworkId, rayPose, deltaTime)`를 Master Client 권위 객체나 화재 객체에 보낸다.
- 더 엄격한 구현: Master Client가 소화기의 `[Networked]` ray pose와 `IsFiring`을 보고 화재 raycast와 `TakeExtinguish()`를 계산한다.
- VR 협동 게임에서는 소화기 pose를 이미 동기화하므로 두 번째가 더 일관적이다.

검증:

- 어느 플레이어가 분사해도 모든 클라이언트의 화재 단계가 동일하게 변한다.
- Master Client가 아닌 피어에서 `ExtinguishProgress`를 직접 쓰지 않는다.

### `Assets/02_Scripts/Extinguisher/Extinguisher.cs`

현재 상태:

- Host Mode 기준으로 `StateAuthority`가 grab/firing/pose 요청을 받아 최종 상태를 쓴다.
- 들고 있는 플레이어는 `HeldBy`로 기록하지만, 실제 pose는 RPC로 StateAuthority에 전달한다.

Shared Mode 방향:

- 소화기는 플레이어 조작 물체이므로 잡은 플레이어가 `StateAuthority`를 획득하는 구조가 적합하다.
- `NetworkObject.Allow State Authority Override`를 켠다.
- grab 시퀀스는 `RequestStateAuthority()` 성공 후 `IsHeld`, `HeldBy`, pose를 권위자가 직접 쓴다.
- 이미 다른 플레이어가 들고 있으면 grab을 거부한다.
- release 시에는 `IsHeld = false`, `HeldBy = PlayerRef.None`, `IsFiring = false`를 쓴 뒤 권위 유지 정책을 정한다.

권위 유지 정책 후보:

- 후보 A: 마지막으로 든 플레이어가 계속 권위를 가진다. 구현이 단순하지만 플레이어가 나가면 월드 물체 상태 회수가 필요하다.
- 후보 B: 놓을 때 `ReleaseStateAuthority()`하고 Master Client가 필요 시 회수한다. 월드 안정성은 좋지만 권위 전환 타이밍 검증이 필요하다.
- 추천: 후보 B. 소화기는 월드 물체이므로 들고 있지 않을 때는 Master Client 권위로 돌아가는 편이 이후 문/화재 판정과 일관된다.

분사와 화재 판정:

- `IsFiring`, ray origin pose, 안전핀 상태는 소화기 권위자가 쓴다.
- 실제 화재 진행도 변경은 화재/Master Client 권위가 계산한다.
- 소화기 권위자가 직접 `FireObject.TakeExtinguish()`를 호출하는 경로는 제거한다.

검증:

- 잡은 플레이어 손에서 지연이 적고, 상대에게 pose가 복제된다.
- 동시에 grab을 시도해도 한 명만 성공한다.
- 놓은 뒤 다른 플레이어가 다시 권위를 획득할 수 있다.
- 소화기를 든 플레이어가 나가도 소화기가 방에서 사라지지 않는다.

### `Assets/02_Scripts/Extinguisher/SafetyPinGrabCondition.cs`

현재 상태:

- `Extinguisher`의 네트워크 상태를 읽어 안전핀 grab 가능 여부를 판단한다.

Shared Mode 방향:

- 큰 구조는 유지한다.
- `IsHeldByLocalPlayer`는 `HeldBy == Runner.LocalPlayer`만으로 충분한지 확인한다.
- grab 필터는 권위 요청 성공 전/후 타이밍 때문에 낙관적으로 열리면 안 된다. 소화기 본체 grab이 확정되기 전 안전핀을 뽑는 상황을 막는다.
- 디버그 로그가 너무 많으면 VR 상호작용 중 프레임과 로그가 흔들릴 수 있으므로 구현 단계에서 정리한다.

검증:

- 로컬 플레이어가 실제로 들고 있는 소화기의 안전핀만 뽑힌다.
- 다른 플레이어가 들고 있는 소화기의 안전핀은 로컬에서 선택되지 않는다.

### `Assets/02_Scripts/Interaction/FireGrabController.cs`

현재 상태:

- `FireObject.OnExtinguished` 이벤트를 보고 grab 가능 여부를 로컬에서 바꾼다.

Shared Mode 방향:

- 기본 구조는 유지 가능하다.
- 단, `OnExtinguished` 이벤트는 각 클라이언트 `Render()`에서 발생할 수 있으므로 상태 변경이 여러 번 호출되어도 안전해야 한다.
- grab 가능 여부는 `FireObject`의 네트워크 완료 상태를 직접 읽는 방식으로 단순화하는 것도 가능하다.

검증:

- 모든 클라이언트에서 화재 진화 후 동일하게 grab 가능해진다.

### `Assets/02_Scripts/NPC/NPCController.cs`

현재 상태:

- 이미 `NetworkBehaviour`이며 Host Mode 기준 StateAuthority만 `NavMeshAgent`를 구동한다.
- 요청 RPC는 `RpcTargets.StateAuthority`로 모인다.
- `CanAcceptWorldStateRequest()`가 `requester == default`만 허용해서 Host-owned trigger 직접 호출을 전제로 한다.

Shared Mode 방향:

- NPC는 월드 규칙 권위이므로 `Is Master Client` 씬 오브젝트로 설정한다.
- `Allow State Authority Override`는 끈다.
- `HasStateAuthority` 분기와 `[Networked]` 상태 구조는 대부분 유지 가능하다.
- 다만 요청 검증은 Host Mode의 `default requester` 전제를 버리고, Shared Mode에서 모든 피어 요청을 명시적으로 검증해야 한다.

요청 검증 기준:

- `RequestFollowPlayer(player)`는 `info.Source == player`일 때만 허용한다.
- 목적지/사망/대사 요청은 트리거가 Master Client 권위에서 직접 호출하거나, 요청자가 실제 트리거 조건을 만족했는지 Master Client가 재검증한다.
- 단순히 `RpcSources.All` 요청을 모두 받으면 임의 클라이언트가 NPC를 이동/사망시킬 수 있으므로 금지한다.

추적 대상:

- 기존처럼 `Runner.GetPlayerObject(player)`로 `NetworkPlayerAvatar`를 찾는다.
- Shared Mode 아바타 스폰 구조가 바뀌면 이 조회가 모든 피어에서 일관되게 동작하는지 먼저 검증한다.

문 열기:

- `NetworkDoor.Open()`이 Master Client 권위 문에 대해 호출되어야 한다.
- NPC와 문이 모두 `Is Master Client`면 현재 호출 구조가 가장 단순하다.

검증:

- NPC NavMeshAgent는 Master Client에서만 enabled다.
- Master Client가 바뀌어도 NPC 권위가 새 Master Client로 이동한다.
- 새 Master Client에서 NPC가 멈추지 않고 계속 이동하거나 안전하게 재시작한다.

### `Assets/02_Scripts/NPC/NPCShoulderGrabTrigger.cs`

현재 상태:

- 로컬 select 이벤트에서 `Runner.LocalPlayer`로 `RequestFollowPlayer()`를 호출한다.

Shared Mode 방향:

- 로컬 입력 이벤트는 유지한다.
- NPC의 StateAuthority가 Master Client이므로 RPC 요청은 Master Client의 NPC로 전달되어야 한다.
- `RequestFollowPlayer(localPlayer)`는 유지 가능하지만, NPC 쪽에서 `info.Source == requestedPlayer` 검증이 반드시 필요하다.
- 햅틱은 로컬 피드백이므로 네트워크화하지 않는다.

검증:

- 어깨를 잡은 플레이어를 NPC가 따라간다.
- 다른 플레이어의 PlayerRef로 위조 요청하는 경로가 막힌다.

### `Assets/02_Scripts/NPC/NPCDestinationSettingTrigger.cs`

현재 상태:

- `NetworkBehaviour`이고 `HasStateAuthority`에서만 1회 처리한다.
- 트리거가 직접 NPC 목적지/대사를 요청한다.

Shared Mode 방향:

- 목적지 트리거도 월드 규칙이므로 `Is Master Client` 씬 오브젝트로 둔다.
- Master Client만 `OnTriggerEnter` 결과를 확정한다.
- `_doorTarget`, `_destination` legacy Transform 경로는 Shared Mode 구현 전에 제거하는 편이 낫다. ID 기반이 네트워크 일관성이 좋다.
- `HasEntered`는 현재처럼 `[Networked]`로 유지한다.

검증:

- NPC가 트리거에 들어갔을 때 한 번만 목적지가 바뀐다.
- Master Client가 아닌 피어의 로컬 trigger event가 결과 상태를 바꾸지 않는다.

### `Assets/02_Scripts/NPC/NPCDeadTrigger.cs`

현재 상태:

- `HasStateAuthority`에서만 사망 처리한다.

Shared Mode 방향:

- `Is Master Client` 씬 오브젝트로 둔다.
- 사망 조건이 NPC collider 진입뿐이라면 현재 구조를 유지할 수 있다.
- 플레이어 피해나 연기 피해로 NPC가 죽는 구조가 추가되면 Master Client가 원인 판정을 직접 해야 한다.

검증:

- 사망이 모든 클라이언트에서 한 번만 재생된다.

### `Assets/02_Scripts/NPC/NPCAnimationEvents.cs`, `NPCStateMachineBehaviour.cs`

Shared Mode 방향:

- 권위 상태를 바꾸는 Animation Event는 NPC `HasStateAuthority`에서만 유효해야 한다.
- 비권위 피어에서는 애니메이션 이벤트가 로컬 시각/청각 효과만 처리하도록 제한한다.
- `FinishOpeningDoor()` 같은 월드 상태 변경 이벤트는 Master Client 권위 NPC에서만 문 상태를 바꿔야 한다.

검증:

- 문 열기 완료 이벤트가 모든 클라이언트에서 중복 실행되지 않는다.

### `Assets/02_Scripts/Door/NetworkDoor.cs`

현재 상태:

- `[Networked] IsOpen`을 가진 단순 문이며 `HasStateAuthority`에서만 열린다.

Shared Mode 방향:

- 일반 문은 `Is Master Client` 씬 오브젝트로 둔다.
- NPC가 문을 여는 경우 NPC와 문 권위가 모두 Master Client라면 현재 `Open()` 구조를 유지할 수 있다.
- 플레이어가 직접 여는 문이라면 플레이어가 문 권위를 획득할지, Master Client에 요청할지 별도로 정한다. 현재 NPC 문은 Master Client 권위가 더 단순하다.

검증:

- Master Client 변경 후에도 문 상태가 유지된다.

### `Assets/02_Scripts/Network/NetworkEmergencyDoor.cs`

현재 문제:

- `MonoBehaviour`이고 네트워크 상태가 없다.
- 현재는 `NetworkPlayerAvatar`의 InputAuthority RPC를 통해 Host Mode StateAuthority가 static dictionary 문을 갱신하고, 다시 All RPC로 각 클라이언트 문 각도를 맞춘다.
- Shared Mode에서 InputAuthority 중계가 깨진다.

Shared Mode 방향:

- `NetworkEmergencyDoor` 자체를 `NetworkBehaviour`로 전환하는 것이 맞다.
- 문 각도 `_currentOpenAngle`을 `[Networked]`로 둔다.
- 플레이어가 잡고 미는 동안 문 권위 정책을 선택한다.

권위 후보:

- 후보 A: 문은 Master Client 권위, 모든 플레이어 push delta는 Master Client로 RPC 요청. 충돌 제어가 쉽고 상태가 안정적이다.
- 후보 B: 문을 잡은 플레이어가 `RequestStateAuthority()`로 권위를 획득. 반응성이 좋지만 두 플레이어 동시 조작과 release 회수가 복잡하다.
- 추천: 비상문은 게임 진행에 영향을 주는 환경 상태이므로 후보 A를 우선한다. VR 손맛이 부족할 때만 후보 B를 검토한다.

검증:

- 두 플레이어가 문을 밀어도 각도가 하나의 네트워크 값으로 수렴한다.
- 아바타 중계 없이 문 자체가 네트워크 상태를 가진다.

### `Assets/02_Scripts/Smoke/SmokeArea.cs`, `Assets/02_Scripts/Player/PlayerCough.cs`

현재 상태:

- 연기 진입/이탈과 기침 오디오는 로컬 처리다.

Shared Mode 방향:

- 단순 기침 오디오만 목적이면 로컬 유지가 맞다.
- 체력, 사망, 구조 실패, 점수에 영향을 주면 Master Client 권위 `NetworkPlayerStatus` 같은 별도 플레이어 상태 객체가 필요하다.
- `SmokeArea`가 직접 로컬 `PlayerCough`만 호출하는 구조와, Master Client가 `NetworkPlayerAvatar` 위치로 연기 내부 여부를 판정하는 구조를 분리한다.
- Shared Mode에서도 로컬 HMD 높이 기반 crouch 판정은 개인 피드백에는 사용 가능하지만, 피해 회피 판정에 쓰려면 crouch 상태를 네트워크 플레이어 상태로 올려야 한다.

검증:

- 기침만 있으면 각 로컬에서 자연스럽게 재생된다.
- 피해가 생기면 Master Client 기준으로 모든 클라이언트의 생존 상태가 동일하다.

### `Assets/02_Scripts/Player/PlayerIdentifier.cs`, `PlayerType.cs`

Shared Mode 방향:

- 공유 게임플레이의 식별자는 `PlayerType.Player1/Player2`가 아니라 `PlayerRef`다.
- UI 표시나 로컬 테스트 편의용으로는 유지할 수 있지만, NPC/소화기/연기/문 판정에는 사용하지 않는다.
- 방 생성자/참가자 구분도 `PlayerType`에 섞지 않는다.

검증:

- 코드 검색으로 공유 로직에서 `PlayerType` 기반 권위 판정이 사라진다.

### `Assets/02_Scripts/Player/PlayerAvatarLocomotionAnimator.cs`, `PlayerAvatarHandTargets.cs`, `PlayerAvatarCameraFollower.cs`

Shared Mode 방향:

- 계속 로컬 XR Origin 보조 스크립트로 유지한다.
- `NetworkPlayerAvatar`가 StateAuthority일 때 이 로컬 스크립트들에서 pose/hand/animation 값을 읽어 네트워크 상태에 쓴다.
- 카메라 follower는 네트워크 대상이 아니다.

검증:

- 로컬 XR Origin은 네트워크 prefab에 묶이지 않고, 자기 클라이언트 입력 소스로만 남는다.

## 권장 작업 순서

1. `FusionRoomConnector`를 `GameMode.Shared` 중심으로 바꾸고 방 생성/참가 의미를 분리한다.
   검증: 두 클라이언트가 같은 세션에 들어오고 Master Client만 씬을 로드한다.

2. `NetworkAvatarSpawner`, `NetworkPlayerAvatar`, `NetworkAvatarInputProvider`, `VrAvatarNetworkInput`를 Shared Mode 아바타 소유 구조로 재작성한다.
   검증: 각 플레이어가 자기 아바타의 StateAuthority이며 상대에게 pose가 보인다.

3. 게임 시작/룸 준비 상태를 `NetworkPlayerAvatar` static 로직에서 Master Client 권위 룸 상태 객체로 분리한다.
   검증: Shared Mode에서 `InputAuthority` 없이 두 플레이어 준비 후 씬 전환된다.

4. NPC, 목적지 트리거, 사망 트리거, 일반 문을 Master Client 권위 씬 오브젝트로 확정한다.
   검증: `runner.IsServer` 없이 Master Client만 NavMeshAgent와 월드 트리거 결과를 확정한다.

5. 소화기를 플레이어 획득 권위 방식으로 바꾼다.
   검증: grab 시 권위 획득, release 시 권위 반환/회수, 동시 grab 충돌 처리가 된다.

6. 화재 진화 판정을 Master Client 권위로 고정하고, 소화기 분사 상태/pose를 입력으로 사용한다.
   검증: 어느 플레이어가 분사해도 화재 진행도가 하나로 수렴한다.

7. 비상문을 `NetworkBehaviour`로 전환하고 문 각도를 `[Networked]` 상태로 만든다.
   검증: 아바타 RPC 중계 없이 문 각도가 모든 클라이언트에서 동일하다.

8. 연기/기침은 로컬 피드백과 게임 결과 판정을 분리한다.
   검증: 기침은 로컬, 피해/사망은 Master Client 기준으로 동기화된다.

9. `PlayerType`, Host/Client role, `InputAuthority`, `runner.IsServer` 사용처를 검색해 Shared Mode에 남으면 안 되는 의존을 정리한다.
   검증: 남은 사용처가 UI 표시나 Server Mode 비호환 주석이 아니라 실제 Shared Mode 설계와 맞는지 확인한다.

## 구현 시 반드시 확인할 검색 키워드

- `GameMode.Host`
- `GameMode.Client`
- `runner.IsServer`
- `Runner.IsServer`
- `HasInputAuthority`
- `InputAuthority`
- `RpcSources.InputAuthority`
- `GetInput(`
- `Object.InputAuthority`
- `LobbyRoomRole.Host`
- `PlayerType.Player1`
- `PlayerType.Player2`
- `RpcTargets.StateAuthority`
- `AllowStateAuthorityOverride`
- `RequestStateAuthority`
- `ReleaseStateAuthority`

이 중 `RpcTargets.StateAuthority`는 Shared Mode에서도 사용할 수 있지만, 대상 오브젝트의 StateAuthority 정책이 명확해야 한다.

## 비효율 또는 논리 오류 가능성 검토

### 오류 1: 모든 것을 Master Client 권위로 몰아넣기

NPC, 화재, 문, 트리거는 Master Client 권위가 맞지만, 플레이어 아바타와 손에 든 소화기 pose까지 Master Client가 처리하면 VR 조작 지연이 커진다. 따라서 월드 규칙과 직접 조작 물체를 분리해야 한다.

### 오류 2: Host Mode 코드를 `GameMode.Shared`로만 바꾸기

`InputAuthority`는 Shared Mode에 적용되지 않으므로, `HasInputAuthority`와 `GetInput()`을 남긴 채 모드만 바꾸면 아바타 입력, 준비 버튼, 비상문 조작이 깨진다.

### 오류 3: 소화기 권위자가 화재 상태를 직접 수정하기

잡은 플레이어가 소화기 StateAuthority가 되는 것은 좋지만, 그 플레이어가 화재 진행도까지 쓰면 월드 규칙 권위가 분산된다. 화재 상태는 Master Client 권위가 써야 한다.

### 오류 4: `Allow State Authority Override`를 모든 씬 오브젝트에 켜기

편해 보이지만 NPC/화재/트리거/문 권위를 아무 플레이어나 가져갈 수 있으면 결과가 흔들린다. override는 플레이어가 직접 조작해야 하는 물체에만 제한적으로 사용한다.

### 오류 5: 트리거 판정을 모든 클라이언트에서 믿기

Shared Mode에서도 물리 trigger event는 각 클라이언트에서 발생할 수 있다. 최종 결과는 Master Client가 확정하거나, 최소한 Master Client 권위 오브젝트에서만 `[Networked]` 상태를 써야 한다.

### 오류 6: Master Client 변경을 고려하지 않기

Shared Mode는 Master Client가 나가면 새 Master Client가 배정된다. NPC/화재/문 같은 씬 오브젝트는 `Is Master Client` 설정으로 권위가 이전되게 해야 하며, 이전 후 NavMeshAgent, 문 상태, 진행도 상태가 정상 복구되는지 검증해야 한다.

### 오류 7: 아바타 스폰 중복

Host Mode처럼 한 피어가 전체 아바타를 스폰하던 구조를 Shared Mode에서 각 피어 로컬 스폰으로 바꾸면 씬 로드/재접속 시 중복 스폰 위험이 생긴다. `Runner.GetPlayerObject(Runner.LocalPlayer)`와 로컬 캐시를 모두 확인하는 중복 방지가 필요하다.

### 오류 8: RPC source를 과하게 열어두기

`RpcSources.All`은 편하지만, 누가 어떤 상태 변경을 요청할 수 있는지 검증하지 않으면 임의 클라이언트가 NPC 이동, 사망, 문 열림, 화재 진화 요청을 보낼 수 있다. Shared Mode에서는 서버가 없으므로 요청 검증을 코드에 명시해야 한다.

## 최종 판단

Host Mode 계획서의 핵심인 "공유 상태는 네트워크화하고 로컬 피드백은 로컬로 둔다"는 방향은 유지한다. Shared Mode 재작성에서 바뀌어야 할 핵심은 권위 배치다.

추천 아키텍처:

- 플레이어 아바타: 각 플레이어 StateAuthority
- 소화기: 잡은 플레이어 StateAuthority, 놓으면 Master Client 회수 또는 release
- 화재: Master Client StateAuthority
- NPC: Master Client StateAuthority
- 목적지/사망 트리거: Master Client StateAuthority
- 일반 문: Master Client StateAuthority
- 비상문: 우선 Master Client StateAuthority, 필요 시 조작자 권위 획득으로 개선
- 연기 기침: 로컬 피드백
- 연기 피해/생존/점수: Master Client StateAuthority

이 구조가 현재 프로젝트의 협력 VR 요구사항에 가장 단순하고, Host Mode 리팩토링에서 이미 만든 `[Networked]` 상태 구조를 최대한 재사용하면서 Shared Mode의 권위 모델과 충돌하지 않는다.

// 260703 PM 04:47
1. FusionRoomConnector.cs, NetworkAvatarSpawner.cs, NetworkEmergencyDoor.cs, PlayerAvatarLocomotionAnimator.cs, PlayerAvatarHandTargets.cs, PlayerAvatarCameraFollower.cs 스크립트는 내 담당이 아니고 내가 만든 스크립트가 아니라서 코드 재작성 계획에서 제외해야할것같아
2. 대신에, FusionRoomConnector.cs, NetworkAvatarSpawner.cs는 여전히 우리 프로젝트의 포톤 시스템의 코어 역할과도 같아서 여전히 자주 참고는 해야할것같아.
3. 내가 제외한 스크립트들을 제외한뒤에 수정해야 할 스크립트들을 리스트로 정리해줘. 여전히 재작성할 필요가 없는 스크립트가 그 리스트에 끼어있다면 내가 추가적으로 검토해줄게
4. 이 문서의 마지막 줄에 쭉 이어서 작성해줘

## 최종 수정 대상 스크립트 목록

아래 파일만 Shared Mode 재작성/검토 대상으로 삼는다. 이 목록 밖의 스크립트는 직접 수정하지 않는다.

1. `Assets/02_Scripts/Fire/FireObject.cs`
2. `Assets/02_Scripts/Extinguisher/Extinguisher.cs`
3. `Assets/02_Scripts/Extinguisher/SafetyPinGrabCondition.cs`
4. `Assets/02_Scripts/Interaction/FireGrabController.cs`
5. `Assets/02_Scripts/NPC/NPCController.cs`
6. `Assets/02_Scripts/NPC/NPCShoulderGrabTrigger.cs`
7. `Assets/02_Scripts/NPC/NPCDestinationSettingTrigger.cs`
8. `Assets/02_Scripts/NPC/NPCDeadTrigger.cs`
9. `Assets/02_Scripts/NPC/NPCAnimationEvents.cs`
10. `Assets/02_Scripts/NPC/NPCStateMachineBehaviour.cs`
11. `Assets/02_Scripts/Door/NetworkDoor.cs`
12. `Assets/02_Scripts/Smoke/SmokeArea.cs`
13. `Assets/02_Scripts/Player/PlayerCough.cs`
14. `Assets/02_Scripts/Player/PlayerIdentifier.cs`

### 목록 적용 기준

- `FusionRoomConnector.cs`, `NetworkAvatarSpawner.cs`, `NetworkEmergencyDoor.cs`, `PlayerAvatarLocomotionAnimator.cs`, `PlayerAvatarHandTargets.cs`, `PlayerAvatarCameraFollower.cs`는 직접 수정 대상에서 제외한다.
- `FusionRoomConnector.cs`와 `NetworkAvatarSpawner.cs`는 Photon Runner, 세션, 씬 로드, 플레이어 아바타 스폰 구조를 이해하기 위한 참고 파일로만 사용한다.
- `NetworkPlayerAvatar.cs`, `NetworkAvatarInputProvider.cs`, `VrAvatarNetworkInput.cs`도 위 최종 목록에 없으므로 직접 수정하지 않는다.
- 최종 구현 계획은 NPC, 화재, 소화기, 문, 연기/플레이어 상태 보조 스크립트 안에서 해결 가능한 범위로 제한한다.

### 우선순위

1. `Assets/02_Scripts/NPC/NPCController.cs`
2. `Assets/02_Scripts/NPC/NPCShoulderGrabTrigger.cs`
3. `Assets/02_Scripts/NPC/NPCDestinationSettingTrigger.cs`
4. `Assets/02_Scripts/NPC/NPCDeadTrigger.cs`
5. `Assets/02_Scripts/NPC/NPCAnimationEvents.cs`
6. `Assets/02_Scripts/NPC/NPCStateMachineBehaviour.cs`
7. `Assets/02_Scripts/Door/NetworkDoor.cs`
8. `Assets/02_Scripts/Fire/FireObject.cs`
9. `Assets/02_Scripts/Extinguisher/Extinguisher.cs`
10. `Assets/02_Scripts/Extinguisher/SafetyPinGrabCondition.cs`
11. `Assets/02_Scripts/Interaction/FireGrabController.cs`
12. `Assets/02_Scripts/Smoke/SmokeArea.cs`
13. `Assets/02_Scripts/Player/PlayerCough.cs`
14. `Assets/02_Scripts/Player/PlayerIdentifier.cs`

이 우선순위는 공유 월드 결과에 직접 영향을 주는 NPC/문/화재/소화기 계층을 먼저 보고, 로컬 피드백 성격이 강한 연기/기침/플레이어 식별 계층을 뒤로 둔 것이다.
