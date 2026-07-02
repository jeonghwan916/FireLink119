# FireLink119 Multiplayer VR Avatar Guide

이 문서는 FireLink119 프로젝트의 로비 입장, Photon Fusion 룸 생성/입장, XR Origin 기반 로컬 아바타, NetworkPlayerAvatar 기반 상대 아바타, 그리고 VR 손 IK 동기화 구조를 설명한다.

처음 프로젝트를 보는 사람이 "왜 이런 방식으로 만들었는지", "어떤 오브젝트를 어디에 배치해야 하는지", "문제가 생겼을 때 어디를 봐야 하는지"를 빠르게 파악하는 것을 목표로 한다.

## 전체 구조 요약

게임 흐름은 다음 순서다.

```text
LobbyScene
-> Host 또는 Client 선택
-> 4자리 Room Code 입력
-> Photon Fusion Host/Client 시작
-> RoomScene 로드
-> 각 플레이어가 NetworkPlayerAvatar 생성
-> 로컬 XR Origin 아바타와 네트워크 아바타가 동기화
```

중요한 점은 Host도 플레이어이고 Client도 플레이어라는 점이다. Host는 방을 만드는 동시에 자기 입력을 보내고, Client는 같은 방 코드로 들어와 자기 입력을 보낸다.

## 씬 구조

### LobbyScene

LobbyScene에는 로컬 VR 조작을 위한 XR Origin과 로비 UI가 있어야 한다.

필수 구조 예시:

```text
LobbyScene
├─ EventSystem
├─ XR Interaction Manager
├─ XR Origin (XR Rig)
│  ├─ Camera Offset
│  │  ├─ Main Camera
│  │  ├─ Left Controller
│  │  │  └─ LeftHandTarget
│  │  └─ Right Controller
│  │     └─ RightHandTarget
│  ├─ Locomotion
│  └─ Meshy_AI_Character_output
│     ├─ Armature
│     ├─ char1
│     └─ Rig_Arms
├─ RoomButtons
├─ Room Scene Load Buttons
├─ Host Room Num
└─ Client Room Num
```

LobbyScene의 `XR Origin (XR Rig)`는 로컬 플레이어가 로비에서 버튼을 누르고 넘패드를 조작하기 위해 필요하다. 우리는 로비부터 VR 조작을 사용하므로, 로비에도 XR Origin이 미리 배치되어야 한다.

### RoomScene

RoomScene에도 로컬 VR 조작용 XR Origin이 있어야 한다.

```text
RoomScene
├─ EventSystem
├─ XR Interaction Manager
├─ XR Origin (XR Rig)
└─ 기타 룸 오브젝트
```

상대방에게 보이는 아바타는 RoomScene에 직접 배치하지 않는다. Photon Fusion이 `NetworkPlayerAvatar.prefab`을 런타임에 생성한다.

## 로비 UI 흐름

### RoomSceneLoadButtons

파일:

```text
Assets/02_Scripts/UI/RoomSceneLoadButtons.cs
```

역할:

- `Host Button` 선택 감지
- `Client Button` 선택 감지
- 선택 결과를 `LobbyRoomCodeFlow`로 전달

이 스크립트는 방을 직접 만들지 않는다. 버튼 선택만 담당한다.

왜 분리했는가:

- 버튼 클릭 처리와 넘패드 입력 처리를 섞으면 스크립트 하나가 너무 많은 역할을 갖게 된다.
- 이후 UI 모양이 바뀌어도 버튼 선택 로직과 방 코드 입력 로직을 따로 수정할 수 있다.

필수 컴포넌트:

```text
Room Scene Load Buttons
├─ RoomSceneLoadButtons
├─ LobbyRoomCodeFlow
└─ FusionRoomConnector
```

주의:

`RoomButtons`가 아니라 `Room Scene Load Buttons`에 붙어 있어야 한다. `RoomButtons`는 시각적 버튼 그룹 역할이고, 실제 방 입장 흐름은 `Room Scene Load Buttons`에서 관리한다.

### LobbyRoomCodeFlow

파일:

```text
Assets/02_Scripts/UI/LobbyRoomCodeFlow.cs
```

역할:

- Host/Client 선택에 맞는 넘패드 활성화
- 입력값을 4자리 숫자로 제한
- 빈 입력 상태에서 Backspace를 누르면 넘패드 닫기
- Enter 제출 시 `FusionRoomConnector.StartRoom()` 호출

넘패드 이름 기본값:

```text
Host Room Num
Client Room Num
```

동작 순서:

```text
Host Button 선택
-> Host Room Num 활성화
-> 4자리 숫자 입력
-> Enter
-> FusionRoomConnector.StartRoom(Host, roomCode)
```

```text
Client Button 선택
-> Client Room Num 활성화
-> 4자리 숫자 입력
-> Enter
-> FusionRoomConnector.StartRoom(Client, roomCode)
```

왜 4자리 숫자를 강제하는가:

- 방 코드는 Photon Fusion SessionName으로 사용된다.
- Host와 Client가 같은 SessionName을 사용해야 같은 방에 들어간다.
- 입력 규칙이 느슨하면 다른 방을 만들거나 입장에 실패하기 쉽다.

## Photon Fusion 연결 흐름

### FusionRoomConnector

파일:

```text
Assets/02_Scripts/Network/FusionRoomConnector.cs
```

역할:

- Host/Client 모드 결정
- `NetworkRunner` 런타임 생성
- Fusion 입력 제공 활성화
- 룸 씬 로드
- 아바타 입력 수집 컴포넌트와 스폰 컴포넌트 연결

핵심 흐름:

```text
StartRoom(role, roomCode)
-> roomCode 검증
-> RoomScene Build Index 찾기
-> NetworkRunner 생성
-> NetworkAvatarInputProvider 추가
-> NetworkAvatarSpawner 추가
-> NetworkSceneManagerDefault 추가
-> runner.StartGame()
```

StartGame 설정:

```text
Host 선택: GameMode.Host
Client 선택: GameMode.Client
SessionName: 로비에서 입력한 4자리 코드
Scene: RoomScene
PlayerCount: 2
```

중요 Inspector 값:

```text
Room Scene Name: RoomScene
Max Players: 2
Player Avatar Prefab: NetworkPlayerAvatar
Player Spawn Origin: 기본 시작 좌표
Player Spawn Spacing: 플레이어 간 간격
```

`Player Avatar Prefab`이 비어 있으면 플레이어는 방에 들어가도 상대 아바타가 생성되지 않는다.

### NetworkRunnerCallbacksBehaviour

파일:

```text
Assets/02_Scripts/Network/NetworkRunnerCallbacksBehaviour.cs
```

역할:

- Fusion의 `INetworkRunnerCallbacks` 기본 빈 구현 제공

왜 필요한가:

Fusion 콜백 인터페이스는 구현해야 할 메서드가 많다. 실제로 필요한 것은 `OnInput`, `OnPlayerJoined`, `OnPlayerLeft` 정도인데, 매 스크립트마다 모든 콜백을 빈 메서드로 작성하면 코드가 지저분해진다.

이 추상 클래스를 상속하면 필요한 콜백만 override하면 된다.

### NetworkAvatarSpawner

파일:

```text
Assets/02_Scripts/Network/NetworkAvatarSpawner.cs
```

역할:

- 플레이어가 방에 들어왔을 때 `NetworkPlayerAvatar` 생성
- 플레이어가 나가면 해당 아바타 제거
- 플레이어별 NetworkObject 매핑 관리

중요한 규칙:

```text
runner.IsServer == true인 쪽만 Spawn한다
```

이유:

Photon Fusion에서 네트워크 오브젝트를 모두가 각자 Spawn하면 클라이언트마다 다른 오브젝트가 생길 수 있다. Host/Server 권한을 가진 쪽이 Spawn해야 같은 NetworkObject가 모든 클라이언트에 복제된다.

## 로컬 아바타와 네트워크 아바타 차이

### 로컬 아바타

로컬 아바타는 실제 VR 조작과 연결된 아바타다.

위치:

```text
XR Origin (XR Rig)
└─ Meshy_AI_Character_output
```

역할:

- 내 HMD와 컨트롤러 기준으로 움직임
- 내 화면에서 보이는 팔/몸 역할
- `PlayerAvatarLocomotionAnimator`가 이동 입력으로 Blend Tree를 구동
- `PlayerAvatarHandTargets`가 손 IK Target 좌표를 네트워크 입력으로 제공

### 네트워크 아바타

네트워크 아바타는 상대방에게 보이는 아바타다.

위치:

```text
Assets/03_Prefabs/Player/NetworkPlayerAvatar.prefab
```

역할:

- Fusion이 방 입장 시 생성
- 각 플레이어의 입력값을 받아 위치, 이동 애니메이션, 손 IK를 재현
- 내 입력 권한이 있는 자기 복제본은 숨김

왜 자기 복제본을 숨기는가:

내가 보는 몸은 XR Origin 안의 로컬 아바타가 담당한다. 동시에 NetworkPlayerAvatar까지 보이면 몸이 두 개 겹치거나 시야를 가릴 수 있다.

## 이동 애니메이션 구조

### PlayerAvatarLocomotionAnimator

파일:

```text
Assets/02_Scripts/Player/PlayerAvatarLocomotionAnimator.cs
```

역할:

- XRI Move Input을 읽음
- 카메라/이동 기준 Transform을 사용해 이동 방향 계산
- 아바타 로컬 기준 `MoveX`, `MoveY` 계산
- Animator 파라미터 적용
- 네트워크 입력 제공자가 읽을 현재 이동 상태 저장

Animator 파라미터:

```text
IsMoving
IsSprinting
MoveX
MoveY
```

왜 로컬 방향으로 변환하는가:

VR에서는 카메라와 몸 방향이 계속 바뀐다. 입력을 월드 방향 그대로 Animator에 넣으면 앞으로 걷는지, 옆으로 걷는지 판단이 틀어질 수 있다. 그래서 입력 방향을 아바타 루트 기준 로컬 X/Z로 변환한다.

### NetworkPlayerAvatar

파일:

```text
Assets/02_Scripts/Network/NetworkPlayerAvatar.cs
```

역할:

- Fusion NetworkBehaviour
- 입력 권한 플레이어의 `VrAvatarNetworkInput`을 StateAuthority에서 읽음
- `[Networked]` 값으로 위치, 회전, 손 Target, 이동 파라미터 저장
- Render 단계에서 복제된 값을 실제 Transform과 Animator에 적용

동기화되는 값:

```text
AvatarPosition
AvatarRotation
LeftHandLocalPosition
LeftHandLocalRotation
RightHandLocalPosition
RightHandLocalRotation
MoveBlend
IsMoving
IsSprinting
```

## VR 손 IK 구조

### 왜 손 본을 직접 동기화하지 않는가

손, 팔꿈치, 어깨 본을 모두 네트워크로 보내면 데이터가 많아지고, 애니메이션과 충돌하기 쉽다.

현재 방식은 손 IK Target만 동기화한다.

```text
LeftHandTarget
RightHandTarget
```

그리고 각 클라이언트의 Animation Rigging Two Bone IK가 팔을 계산한다.

장점:

- 네트워크 전송값이 적다
- 팔꿈치 방향은 Hint로 제어 가능하다
- 로컬 아바타와 네트워크 아바타가 같은 IK 구조를 공유한다
- 추후 손가락 리깅이 생겨도 별도 레이어로 확장하기 쉽다

### 로컬 아바타 IK 구조

로컬 아바타는 컨트롤러를 직접 따라가야 하므로 Target이 컨트롤러 자식이다.

```text
XR Origin (XR Rig)
├─ Camera Offset
│  ├─ Left Controller
│  │  └─ LeftHandTarget
│  └─ Right Controller
│     └─ RightHandTarget
└─ Meshy_AI_Character_output
   └─ Rig_Arms
      ├─ LeftArmIK
      ├─ RightArmIK
      ├─ LeftElbowHint
      └─ RightElbowHint
```

Target의 Local Position/Rotation은 손목 보정값이다. 컨트롤러 자체 위치를 옮기면 XR 상호작용까지 틀어지므로, 보정은 반드시 `LeftHandTarget`, `RightHandTarget`에서 한다.

### 네트워크 아바타 IK 구조

네트워크 아바타에는 실제 VR 컨트롤러가 없다. 따라서 Target은 프리팹 내부에 둔다.

```text
NetworkPlayerAvatar
├─ Rig Builder
├─ Armature
├─ char1
└─ Rig_Arms
   ├─ Rig
   ├─ LeftArmIK
   ├─ RightArmIK
   ├─ LeftElbowHint
   ├─ RightElbowHint
   ├─ LeftHandTarget
   └─ RightHandTarget
```

중요:

```text
NetworkPlayerAvatar > Rig Builder > Rig Layers[0] = Rig_Arms
```

이 연결이 빠지면 IK 오브젝트가 있어도 상대방 팔이 움직이지 않는다.

### Two Bone IK 연결값

LeftArmIK:

```text
Root   = Armature > Hips > Spine02 > Spine01 > Spine > LeftShoulder > LeftArm
Mid    = Armature > Hips > Spine02 > Spine01 > Spine > LeftShoulder > LeftArm > LeftForeArm
Tip    = Armature > Hips > Spine02 > Spine01 > Spine > LeftShoulder > LeftArm > LeftForeArm > LeftHand
Target = Rig_Arms > LeftHandTarget
Hint   = Rig_Arms > LeftElbowHint
```

RightArmIK:

```text
Root   = Armature > Hips > Spine02 > Spine01 > Spine > RightShoulder > RightArm
Mid    = Armature > Hips > Spine02 > Spine01 > Spine > RightShoulder > RightArm > RightForeArm
Tip    = Armature > Hips > Spine02 > Spine01 > Spine > RightShoulder > RightArm > RightForeArm > RightHand
Target = Rig_Arms > RightHandTarget
Hint   = Rig_Arms > RightElbowHint
```

권장 값:

```text
Weight                 = 1
Target Position Weight = 1
Target Rotation Weight = 1
Hint Weight            = 1
Maintain Target Offset = None
```

## 손 Target 네트워크 좌표계

### PlayerAvatarHandTargets

파일:

```text
Assets/02_Scripts/Player/PlayerAvatarHandTargets.cs
```

역할:

- 로컬 Target 월드 위치를 AvatarRoot 기준 로컬 위치로 변환
- 네트워크로 받은 로컬 위치를 네트워크 아바타의 월드 위치로 복원

보내는 쪽:

```text
localPosition = AvatarRoot.InverseTransformPoint(HandTarget.position)
localRotation = Quaternion.Inverse(AvatarRoot.rotation) * HandTarget.rotation
```

받는 쪽:

```text
HandTarget.position = AvatarRoot.TransformPoint(localPosition)
HandTarget.rotation = AvatarRoot.rotation * localRotation
```

왜 로컬 좌표를 쓰는가:

플레이어마다 방 안 스폰 위치와 회전이 다르다. 손 Target을 월드 좌표 그대로 보내면 상대방 캐릭터의 손이 엉뚱한 월드 좌표로 갈 수 있다. 아바타 기준 로컬 좌표를 쓰면 각 캐릭터 몸 기준으로 같은 손 위치를 재현할 수 있다.

## 프리팹 체크리스트

### XR Origin (XR Rig).prefab

확인할 것:

```text
XR Origin (XR Rig)
├─ Camera Offset
│  ├─ Main Camera
│  ├─ Left Controller
│  │  └─ LeftHandTarget
│  └─ Right Controller
│     └─ RightHandTarget
└─ Meshy_AI_Character_output
   ├─ PlayerAvatarLocomotionAnimator
   ├─ PlayerAvatarCameraFollower
   ├─ Rig Builder
   └─ Rig_Arms
```

### NetworkPlayerAvatar.prefab

확인할 것:

```text
NetworkPlayerAvatar
├─ NetworkObject
├─ NetworkPlayerAvatar
├─ Animator
├─ Rig Builder
├─ Armature
├─ char1
└─ Rig_Arms
```

`NetworkPlayerAvatar` 컴포넌트:

```text
Avatar Root = NetworkPlayerAvatar Transform
Animator = NetworkPlayerAvatar Animator
Hide For Input Authority = true
```

`Rig Builder`:

```text
Rig Layers[0] = Rig_Arms
Active = true
```

## 테스트 순서

### 1. 로컬 IK 테스트

목표:

내 VR 컨트롤러를 움직였을 때 내 팔이 따라오는지 확인한다.

확인:

- `LeftHandTarget`, `RightHandTarget`이 컨트롤러 자식인지
- `Rig Builder`에 `Rig_Arms`가 등록되어 있는지
- 팔꿈치가 이상하게 꺾이면 `LeftElbowHint`, `RightElbowHint` 위치를 조정

### 2. 로비 방 입장 테스트

목표:

Host와 Client가 같은 4자리 코드로 같은 RoomScene에 들어가는지 확인한다.

확인:

- Host 입력 코드와 Client 입력 코드가 같은지
- `RoomScene`이 Build Settings에 등록되어 있는지
- `FusionRoomConnector.Player Avatar Prefab`이 `NetworkPlayerAvatar`인지

### 3. 상대 아바타 표시 테스트

목표:

두 플레이어가 같은 방에 들어갔을 때 상대방 모델이 보이는지 확인한다.

확인:

- 콘솔에 `[NetworkAvatarSpawner] Network avatar prefab is not assigned.`가 없는지
- `NetworkPlayerAvatar.prefab`이 Fusion Prefab Table에 등록되어 있는지
- `NetworkObject`가 붙어 있는지

### 4. 상대 팔 동기화 테스트

목표:

내 컨트롤러 움직임이 상대방 화면의 내 아바타 팔에 보이는지 확인한다.

확인:

- `NetworkPlayerAvatar.prefab`의 `Rig Builder > Rig Layers`가 `Rig_Arms`를 참조하는지
- `Rig_Arms` 아래에 `LeftHandTarget`, `RightHandTarget` 이름이 정확한지
- `LeftArmIK`, `RightArmIK`의 Target/Hint가 연결되어 있는지

## 자주 나는 문제

### 방 입장은 되는데 상대방 모델이 안 보인다

가능 원인:

- `FusionRoomConnector.Player Avatar Prefab`이 비어 있음
- 잘못된 오브젝트에 `FusionRoomConnector`를 붙임
- `NetworkPlayerAvatar`가 Fusion Prefab으로 등록되지 않음
- `NetworkObject`가 프리팹에 없음

### 상대방 모델은 보이는데 팔이 안 움직인다

가능 원인:

- `NetworkPlayerAvatar.prefab`의 `Rig Builder > Rig Layers`가 비어 있음
- `LeftHandTarget`, `RightHandTarget` 이름이 다름
- `LeftArmIK`, `RightArmIK` Target/Hint 연결이 빠짐
- `Rig_Arms`가 비활성화됨

### 내 팔은 움직이는데 위치가 이상하다

가능 원인:

- 컨트롤러 자체를 옮김
- `LeftHandTarget`, `RightHandTarget` 보정값이 맞지 않음
- 아바타 몸 위치와 HMD 위치가 맞지 않음

해결 방향:

- `Left Controller`, `Right Controller`는 움직이지 않는다
- `LeftHandTarget`, `RightHandTarget`의 Local Position/Rotation만 조정한다
- 카메라와 아바타 머리/어깨 기준을 먼저 맞춘다

### 팔꿈치가 반대로 꺾인다

가능 원인:

- `LeftElbowHint`, `RightElbowHint` 위치가 잘못됨

해결 방향:

- Hint의 Rotation은 크게 중요하지 않다
- Hint의 Position을 팔꿈치 바깥쪽 + 살짝 뒤쪽으로 둔다
- 팔꿈치가 안쪽으로 꺾이면 Hint를 더 바깥쪽으로 옮긴다

## 현재 설계의 핵심 원칙

1. 로비부터 룸까지 VR 조작을 유지한다.
2. Host와 Client는 4자리 Room Code를 Photon Fusion SessionName으로 공유한다.
3. 로컬 아바타와 네트워크 아바타는 분리한다.
4. 내 화면의 몸은 XR Origin 안의 로컬 아바타가 담당한다.
5. 상대방에게 보이는 몸은 Fusion이 생성한 NetworkPlayerAvatar가 담당한다.
6. 손/팔 동기화는 본 직접 동기화가 아니라 IK Target 동기화로 처리한다.
7. 손 Target 좌표는 월드가 아니라 AvatarRoot 기준 로컬 좌표로 보낸다.
8. 씬에 직접 네트워크 아바타를 배치하지 않고 Fusion Spawn으로 생성한다.
