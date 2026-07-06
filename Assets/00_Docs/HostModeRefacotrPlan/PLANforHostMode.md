## VR 협력 게임 기준 추가 동기화 대상

현재 프로젝트에서 플레이어 아바타를 제외하면, 여러 플레이어가 같은 월드 상태를 봐야 하는 스크립트 중 상당수가 아직 로컬 `MonoBehaviour`로 동작합니다. 협력 VR 게임에서는 한 플레이어의 행동이 다른 플레이어에게 보이지 않으면 게임 상황 판단이 어긋납니다. 따라서 아래 스크립트들은 최종 완성본 단계에서 Photon Fusion 동기화 대상입니다.

## 현재 멀티플레이 구조 기준

- 공유 게임플레이 로직은 `XR Origin (XR Rig)`를 직접 참조하지 않습니다.
- `XR Origin (XR Rig)`는 각 클라이언트의 로컬 입력, 카메라, 컨트롤러, 손 IK Target 제공자입니다.
- 네트워크상 플레이어 대표 객체는 Fusion이 스폰한 `NetworkPlayerAvatar`입니다.
- 다른 플레이어의 위치, 손 위치, 이동 애니메이션은 `NetworkPlayerAvatar`가 표현합니다.
- NPC 추적, 연기 판정, 구조 판정, 소화기 소유권처럼 게임 결과에 영향을 주는 로직은 `PlayerRef`, `InputAuthority`, `Runner.GetPlayerObject(player)` 기준으로 처리합니다.
- `PlayerAvatarLocomotionAnimator`, `PlayerAvatarHandTargets`, `PlayerAvatarCameraFollower`는 로컬 XR Origin 쪽 보조 스크립트로 유지하고, `NetworkPlayerAvatar` 프리팹에는 로컬 입력 수집 스크립트를 붙이지 않습니다.

### 1순위: 화재와 소화기 상호작용

대상 스크립트:

- `Assets/02_Scripts/Fire/FireObject.cs`
- `Assets/02_Scripts/Extinguisher/Extinguisher.cs`
- `Assets/02_Scripts/FireGrabController.cs` // 이건 안함
- `Assets/02_Scripts/Extinguisher/SafetyPinGrabCondition.cs`

재작성이 필요한 이유:

- 현재 화재 진행도, 단계, 완전 진화 여부가 각 클라이언트의 로컬 필드에만 저장됩니다.
- 현재 소화기 분사 여부, 파티클, 오디오, 안전핀 상태도 로컬에서만 처리됩니다.
- 한 플레이어가 불을 꺼도 다른 플레이어 화면에서는 불이 그대로 남거나, 서로 다른 속도로 꺼질 수 있습니다.
- 소화기 위치/회전과 잡힘 상태가 공유되지 않으면 다른 플레이어가 누가 어디에서 어떤 방향으로 분사 중인지 알 수 없습니다.

수정 지침:

- `FireObject`는 룸 씬에 미리 배치된 Fusion 씬 오브젝트로 다룹니다.
- `FireObject`를 `NetworkBehaviour`로 바꾸고, `NetworkObject`가 붙은 GameObject에 둡니다.
- 화재 진행도, 현재 단계, 진화 완료 여부는 `[Networked]` 상태로 둡니다.
- `TakeExtinguish`는 호스트/StateAuthority에서만 실제 진행도를 증가시킵니다.
- 클라이언트는 `fire.TakeExtinguish(Time.deltaTime)`를 직접 호출하지 않습니다.
- `Extinguisher`는 잡힘 상태, 보유자, 안전핀, 분사 여부, 위치/회전을 네트워크 상태로 관리합니다.
- 소화기 보유자는 `PlayerType`이나 로컬 Transform이 아니라 `PlayerRef` 기준으로 기록합니다.
- 그랩/릴리스/분사 입력은 클라이언트에서 발생하더라도, 최종 상태 변경은 호스트가 검증한 뒤 `[Networked]` 상태에 기록합니다.
- 파티클과 오디오는 네트워크로 입자 자체를 복제하지 않고, `IsFiring` 같은 네트워크 상태를 보고 각 클라이언트에서 로컬 재생합니다.
- `FireGrabController`와 `SafetyPinGrabCondition`은 로컬 필드가 아니라 네트워크화된 화재/소화기 상태를 기준으로 grab 가능 여부와 안전핀 조건을 판단합니다.

### 2순위: NPC 행동과 상태

대상 스크립트:

- `Assets/02_Scripts/NPC/NPCController.cs`
- `Assets/02_Scripts/NPC/NPCShoulderGrabTrigger.cs`
- `Assets/02_Scripts/NPC/NPCDestinationSettingTrigger.cs`
- `Assets/02_Scripts/NPC/NPCDeadTrigger.cs`
- `Assets/02_Scripts/NPC/NPCAnimationEvents.cs`
- `Assets/02_Scripts/NPC/NPCStateMachineBehaviour.cs`

재작성이 필요한 이유:

- NPC는 모든 플레이어가 같은 위치와 같은 행동 상태로 봐야 하는 공유 월드 오브젝트입니다.
- 현재 `NPCController.Update()`는 각 클라이언트에서 `NavMeshAgent`, 상태 전환, 애니메이션 파라미터를 독립적으로 계산합니다.
- 어깨 잡기, 목적지 트리거, 사망 트리거가 로컬에서 바로 `NPCController` 상태를 바꿉니다.
- 문 열기 완료 시 `_currentOpeningDoor.SetActive(false)`가 로컬에서 실행되므로, 어떤 클라이언트에서는 문이 사라지고 다른 클라이언트에서는 남을 수 있습니다.
- 랜덤 대사 선택이 클라이언트마다 다르면 같은 상황에서 서로 다른 음성이 재생될 수 있습니다.

수정 지침:

- NPC는 `NetworkObject`를 가진 Fusion 씬 오브젝트로 다룹니다.
- `NPCController`는 `NetworkBehaviour`로 전환하고, 호스트/StateAuthority만 `NavMeshAgent`를 실제로 구동합니다.
- 클라이언트는 NPC 경로 계산을 하지 않고, 네트워크로 받은 위치/회전/상태/애니메이션 파라미터를 렌더링합니다.
- NPC 상태는 `[Networked] NPCState`, `[Networked] NetworkBool IsDead`, `[Networked] NetworkBool IsOpeningDoor`, `[Networked] NetworkBool IsCrouching` 같은 형태로 분리합니다.
- 현재 목표는 Transform 참조를 그대로 네트워크화하기보다, 대상 종류나 목적지 ID를 네트워크 상태로 표현하는 방향이 안전합니다.
- NPC가 플레이어를 따라갈 때는 로컬 `XR Origin`이나 `Player1/Player2` Transform을 목표로 삼지 않습니다.
- NPC의 플레이어 추적 대상은 `PlayerRef`로 저장하고, 호스트가 `Runner.GetPlayerObject(player)`로 얻은 `NetworkPlayerAvatar` Transform을 따라가게 합니다.
- `NPCShoulderGrabTrigger`는 클라이언트에서 바로 `StartFollowingPlayer`를 호출하지 않고, 호스트에 이 `PlayerRef`를 따라가라는 요청을 보냅니다.
- `NPCDestinationSettingTrigger`와 `NPCDeadTrigger`의 `_hasEntered`는 호스트만 확정하거나 네트워크 상태로 관리합니다.
- 문 열림/닫힘 같은 월드 변화는 NPC 내부 로컬 처리에 숨기지 말고, 문 오브젝트 상태도 별도 네트워크 상태로 관리합니다.
- `NPCAnimationEvents`와 `NPCStateMachineBehaviour`는 권위 상태를 변경할 때 호스트 경로에서만 실행되도록 제한하거나, 네트워크 상태를 보고 로컬 시각 효과만 처리하도록 역할을 나눕니다.

### 3순위: 연기 구역과 플레이어 상태 효과

대상 스크립트:

- `Assets/02_Scripts/Smoke/SmokeArea.cs` // 이건 안함
- `Assets/02_Scripts/Player/PlayerCough.cs` // 이건 안함

재작성이 필요한 이유:

- 현재 기침은 로컬 오디오 피드백에 가깝기 때문에 단독으로는 반드시 동기화할 필요가 낮습니다.
- 하지만 연기가 체력 감소, 사망, 구조 실패, 점수, 진행 조건에 영향을 주면 로컬 판정으로 두면 안 됩니다.
- 플레이어 A 화면에서는 연기 피해를 받았고, 호스트나 플레이어 B 기준에서는 피해를 받지 않은 상태가 생길 수 있습니다.

수정 지침:

- 단순 기침 오디오와 화면 효과는 로컬 피드백으로 유지할 수 있습니다.
- 체력, 사망, 구조 가능 여부처럼 게임 결과에 영향을 주는 상태가 추가되면 호스트/StateAuthority가 판정해야 합니다.
- `SmokeArea`는 로컬 `OnTriggerEnter/Exit`만으로 최종 피해 상태를 바꾸지 않고, 호스트가 플레이어의 연기 구역 진입 여부를 확정하는 구조로 바꿉니다.
- 호스트 판정은 로컬 `XR Origin`이 아니라 `NetworkPlayerAvatar` 위치나 `PlayerRef`로 연결된 네트워크 플레이어 상태를 기준으로 합니다.
- `PlayerCough`는 네트워크 게임플레이 상태를 직접 쓰기보다, 네트워크로 확정된 연기 안에 있음 또는 기침 중 상태를 보고 로컬 오디오/시각 효과를 재생하는 역할로 제한합니다.

### 4순위: 플레이어 식별과 로컬 아바타 보조 스크립트

대상 스크립트:

- `Assets/02_Scripts/Player/PlayerIdentifier.cs` // 이건 안함
- `Assets/02_Scripts/Player/PlayerAvatarLocomotionAnimator.cs` // 이건 안함
- `Assets/02_Scripts/Player/PlayerAvatarHandTargets.cs` // 이건 안함
- `Assets/02_Scripts/Player/PlayerAvatarCameraFollower.cs` // 이건 안함

재작성이 필요한 이유:

- 플레이어 아바타의 네트워크 동기화는 이미 `NetworkPlayerAvatar`, `NetworkAvatarInputProvider`, `VrAvatarNetworkInput` 쪽에서 어느 정도 처리하고 있습니다.
- 다만 `PlayerIdentifier`처럼 Player1/Player2를 수동 필드로 구분하는 방식은 네트워크 PlayerRef와 불일치할 수 있습니다.
- 협력 상호작용에서 누가 NPC를 잡았는지, 누가 소화기를 들었는지 판단할 때 로컬 enum만 믿으면 잘못된 소유자 판정이 생길 수 있습니다.

수정 지침:

- 권위 있는 플레이어 식별은 가능하면 Fusion의 `PlayerRef`, `InputAuthority`, `Runner.GetPlayerObject(player)` 기준으로 정리합니다.
- `PlayerIdentifier`는 로컬 편의 정보로만 쓰거나, 네트워크 스폰 시 `PlayerRef`와 일관되게 설정되도록 바꿉니다.
- `PlayerAvatarLocomotionAnimator`와 `PlayerAvatarHandTargets`는 로컬 입력 수집 역할을 유지하고, 네트워크 상태 기록은 기존처럼 `NetworkAvatarInputProvider`와 `NetworkPlayerAvatar`에 맡깁니다.
- `PlayerAvatarCameraFollower`는 로컬 카메라 보조 기능이므로 일반적으로 네트워크 동기화 대상이 아닙니다.
- NPC, Smoke, 소화기처럼 공유 게임플레이에 필요한 플레이어 위치는 `XR Origin`이 아니라 `NetworkPlayerAvatar`를 통해 얻습니다.

## 동기화 대상 선정 기준

- 모든 플레이어가 같은 결과를 봐야 하는 월드 상태라면 네트워크화합니다.
- 한 플레이어의 행동이 다른 플레이어의 진행, NPC, 문, 불, 점수, 생존 여부에 영향을 주면 호스트 권위로 처리합니다.
- 오디오, 햅틱, 카메라 흔들림, UI처럼 특정 플레이어에게만 필요한 피드백은 로컬 처리로 유지합니다.
- 로컬 `XR Origin`은 네트워크 판정 대상이 아니라 입력 소스입니다.
- 공유 판정에서 플레이어가 필요하면 `PlayerRef`와 `NetworkPlayerAvatar`를 기준으로 찾습니다.
- 위치/회전이 중요한 공유 오브젝트는 `NetworkObject`와 `[Networked]` 상태 또는 Fusion Transform 동기화 방식을 사용합니다.
- 입력 요청은 RPC나 `INetworkInput`으로 보내고, 최종 상태는 `[Networked]` 속성으로 남깁니다.
- 클라이언트는 최종 게임 상태를 직접 쓰지 않고, 호스트/StateAuthority가 검증 후 기록합니다.

## 권장 작업 순서

1. `PlayerIdentifier`와 `PlayerType.Player1/Player2` 의존을 줄이고, 공유 로직의 플레이어 식별 기준을 `PlayerRef`로 정리합니다.
2. `NetworkPlayerAvatar`를 게임플레이에서 참조하는 네트워크 플레이어 대표 객체로 확정합니다.
3. `NPCController`를 호스트 권위 NPC 컨트롤러로 전환하고, NPC 추적 대상을 `PlayerRef -> Runner.GetPlayerObject(player)` 기준으로 바꿉니다.
4. NPC 관련 트리거 스크립트가 로컬에서 직접 상태를 바꾸지 않고 호스트 요청 경로를 타도록 수정합니다.
5. `SmokeArea`와 `PlayerCough`를 분리해, 게임 결과 판정은 호스트가 `NetworkPlayerAvatar` 기준으로 처리하고 기침은 로컬 피드백으로 유지합니다.
6. `FireObject`를 Fusion 씬 오브젝트 기반 `NetworkBehaviour`로 전환합니다.
7. `Extinguisher`, `FireGrabController`, `SafetyPinGrabCondition`을 소화기 grab/pose/fire 상태 동기화 구조로 묶습니다.
8. 문, 플레이어 피해, 구조 가능 여부처럼 게임 결과에 영향을 주는 환경 상태를 추가로 네트워크화합니다.
9. 로컬 피드백 전용 스크립트는 네트워크 상태를 읽어서 재생하는 역할로 제한합니다.
