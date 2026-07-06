# PLAN for TestNetworkBootstrap.cs

## Goal

`RoomScene_Test_FireAndExtinguisher` 같은 테스트 대상 씬을 직접 Play하지 않고, 별도 부트스트랩 씬에서 Fusion Shared 모드 세션을 시작한 뒤 네트워크 씬 로드 흐름으로 진입하게 만든다.

이 스크립트의 목적은 테스트 편의용 진입 경로를 만드는 것이다. 소화기, 안전핀, 화재 진화 같은 실제 게임 로직에는 로컬 fallback 분기를 추가하지 않는다.

## Assumptions

- `TestNetworkBootstrap.cs`는 테스트 전용 부트스트랩 씬의 빈 GameObject에 붙인다.
- 테스트 대상 씬은 Build Settings에 등록되어 있어야 한다.
- 네트워크 입력 수집은 기존 `NetworkAvatarInputProvider`를 재사용한다.
- 네트워크 아바타 spawn은 Shared 모드 테스트 전용 spawner를 사용한다.
- 테스트 세션 이름과 테스트 대상 씬 이름은 당장은 Inspector string으로 받는다.
- 나중에 씬 이름 입력은 dropdown/editor tooling으로 바꿀 수 있지만, 이번 계획에는 포함하지 않는다.

## Serialized Fields

```csharp
[Header("Session")]
[SerializeField] private string _sessionName = "Test";
[SerializeField] private int _maxPlayers = 2;

[Header("Scene")]
[SerializeField] private string _targetSceneName = "RoomScene_Test_FireAndExtinguisher";

[Header("Avatar")]
[SerializeField] private NetworkPrefabRef _playerAvatarPrefab;
[SerializeField] private Vector3 _playerSpawnOrigin = Vector3.zero;
[SerializeField] private float _playerSpawnSpacing = 1.5f;
```

필드는 최소한만 둔다. session name과 scene name은 테스트자가 Inspector에서 바꿀 수 있어야 한다.

## Responsibilities

1. 중복 실행 방지
   - `_isStarting` 플래그로 같은 인스턴스에서 `StartGame`이 중복 호출되지 않게 한다.
   - 이미 씬에 `NetworkRunner`가 있으면 새 Runner를 만들지 않고 경고 후 종료한다.

2. 테스트 대상 씬 검증
   - `_targetSceneName`이 비어 있으면 시작하지 않는다.
   - Build Settings에서 `_targetSceneName`과 일치하는 build index를 찾는다.
   - build index를 찾지 못하면 명확한 에러 로그를 남기고 종료한다.

3. Fusion Runner 생성
   - 새 GameObject를 만들고 이름은 `Photon Fusion Test Runner (Shared)` 정도로 둔다.
   - `DontDestroyOnLoad`를 적용한다.
   - `NetworkRunner`를 추가한다.
   - `runner.ProvideInput = true`를 설정한다.
   - 같은 GameObject에 `NetworkSceneManagerDefault`를 추가한다.

4. Shared 테스트용 NetworkAvatar spawn 콜백 구성
   - 같은 GameObject에 `NetworkAvatarInputProvider`를 추가한다.
   - 같은 GameObject에 `TestSharedAvatarSpawner`를 추가한다.
   - `TestSharedAvatarSpawner.Initialize(_playerAvatarPrefab, _playerSpawnOrigin, _playerSpawnSpacing)`를 호출한다.
   - `_playerAvatarPrefab.IsValid`가 false면 시작하지 않는다.

5. Shared 세션 시작
   - `StartGameArgs.GameMode = GameMode.Shared`
   - `StartGameArgs.SessionName = _sessionName`
   - `StartGameArgs.Scene = SceneRef.FromIndex(targetSceneBuildIndex)`
   - `StartGameArgs.SceneManager = sceneManager`
   - `StartGameArgs.PlayerCount = _maxPlayers`
   - `StartGameArgs.IsOpen = true`
   - `StartGameArgs.IsVisible = true`

6. 실패 처리
   - `StartGame` 결과가 실패하면 shutdown reason을 로그로 남긴다.
   - 생성한 Runner GameObject를 정리한다.
   - `_isStarting`을 false로 되돌린다.

## Execution Flow

1. 부트스트랩 씬을 Play한다.
2. 버튼 리스너에서 `TestNetworkBootstrap.StartSharedTestSession()`을 호출한다.
3. target scene build index를 찾는다.
4. Runner GameObject를 생성하고 Fusion 관련 컴포넌트를 붙인다.
5. Shared 전용 avatar spawner를 초기화한다.
6. `runner.StartGame(...)`로 Shared 모드 `"Test"` 또는 Inspector 지정 session에 입장한다.
7. Fusion scene manager가 `_targetSceneName` 씬을 네트워크 씬으로 로드한다.
8. 씬 내 `NetworkObject`들의 `Spawned()`가 호출된다.
9. 씬 로드 완료 후 local player avatar를 spawn하고 `runner.SetPlayerObject`를 설정한다.

## Why This Is Needed

현재 `Extinguisher`는 `Spawned()`, `Runner`, `Runner.LocalPlayer`, `StateAuthority`가 있는 상태를 전제로 한다.

테스트 대상 씬을 직접 Play하면 이 전제가 깨져서 다음 동작이 막힌다.

- 소화기 grab 후 `RequestStateAuthority` 흐름이 실행되지 않는다.
- `HeldBy`가 `Runner.LocalPlayer`로 세팅되지 않는다.
- `SafetyPinGrabCondition`이 로컬 보유자 조건을 만족하지 못한다.
- `SetFiring`이 `IsHeldByLocalPlayer && HasStateAuthority` 조건에서 return 된다.
- 화재 진화도 `Runner.IsSharedModeMasterClient`와 `FireObject.HasStateAuthority` 조건을 통과하지 못한다.

부트스트랩 씬은 이 문제를 프로덕션 로직에 fallback을 추가하지 않고 해결한다.

## Extra Recommendations

- `Application.runInBackground = true`를 `StartSharedTestSession()` 초반에 설정한다.
  - 에디터와 빌드 또는 빌드 2개를 동시에 띄워 테스트할 때 포커스를 잃은 인스턴스가 멈추는 것을 줄인다.

- `_sessionName` 기본값은 `"Test"`로 둔다.
  - 같은 Photon AppId, region, game version을 쓰는 두 실행 인스턴스가 같은 `"Test"` 세션에 들어간다.
  - 팀원이 동시에 테스트할 가능성이 있으면 Inspector에서 `Test_UserName`처럼 바꿔 쓰면 된다.

- `_maxPlayers`는 기본 2로 두되, Inspector field로 둔다.
  - 요구사항은 2명이지만 테스트 중 임시 변경이 필요할 수 있다.

- target scene name은 string으로 유지한다.
  - 현재는 가장 단순하다.
  - 나중에 custom editor에서 Build Settings 씬 dropdown으로 교체하면 오타 리스크를 줄일 수 있다.

## Success Criteria

1. 부트스트랩 씬에서 Play하면 `RoomScene_Test_FireAndExtinguisher`가 Shared 모드로 로드된다.
2. 첫 번째 실행 인스턴스가 session name `"Test"`로 세션을 만든다.
3. 두 번째 실행 인스턴스가 같은 `"Test"` 세션에 입장한다.
4. 세션 최대 인원은 2명으로 제한된다.
5. 각 클라이언트가 자기 local player avatar를 spawn하고 `runner.SetPlayerObject`를 설정한다.
6. 테스트 대상 씬의 소화기 `Extinguisher.Spawned()`가 호출되어 `IsNetworkReady` 조건이 true가 된다.
7. 소화기 grab, 안전핀 pull, 분사 로직이 Shared 모드 권한 흐름 안에서 테스트 가능해진다.

## Non-Goals

- `Extinguisher`에 로컬 fallback을 추가하지 않는다.
- `FireObject`에 로컬 진화 fallback을 추가하지 않는다.
- lobby UI나 room code 입력 UI를 만들지 않는다.
- 자동 입장은 기본 동작으로 만들지 않는다. 입장은 버튼 리스너에서 `StartSharedTestSession()`을 호출해 시작한다.
- target scene dropdown custom editor는 이번 범위에 포함하지 않는다.

## Script Draft

기존 `FusionRoomConnector`는 Host/Client 모드 진입용 구조다. `NetworkRunner`를 만들고, `NetworkAvatarInputProvider`, `NetworkSceneManagerDefault`를 붙인 뒤 `StartGame`을 호출하는 큰 흐름은 재사용할 수 있다.

하지만 기존 `NetworkAvatarSpawner`는 `runner.IsServer`인 피어만 모든 플레이어 아바타를 spawn한다. Shared 모드에는 서버 피어가 없으므로 이 부트스트랩에서는 그대로 쓰면 안 된다. 테스트 부트스트랩은 각 클라이언트가 자기 `Runner.LocalPlayer` 아바타만 spawn하고 `Runner.SetPlayerObject`를 호출하는 Shared 전용 스포너를 같은 파일에 둔다.

현재 `NetworkPlayerAvatar`가 아직 `HasInputAuthority`와 `GetInput` 경로를 일부 사용하므로, 테스트용 최소 동작에서는 `runner.Spawn(..., inputAuthority: runner.LocalPlayer)`를 유지한다. 최종 Shared 리팩토링에서 `NetworkPlayerAvatar`가 StateAuthority 기반으로 완전히 정리되면 이 부분은 제거할 수 있다.

버튼 리스너에서 호출할 함수는 `StartSharedTestSession()`이다.

Unity UI Button이면 Inspector의 `OnClick`에 `TestNetworkBootstrap.StartSharedTestSession`을 연결한다.

XRI 버튼에서 코드로 연결한다면 다음 형태로 호출한다.

```csharp
_testButton.selectEntered.AddListener(_ => _bootstrap.StartSharedTestSession());
```

```csharp
using System.IO;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FireLink119.Network
{
    public sealed class TestNetworkBootstrap : MonoBehaviour
    {
        [Header("Session")]
        [SerializeField] private string _sessionName = "Test";
        [SerializeField] private int _maxPlayers = 2;

        [Header("Scene")]
        [SerializeField] private string _targetSceneName = "RoomScene_Test_FireAndExtinguisher";

        [Header("Avatar")]
        [SerializeField] private NetworkPrefabRef _playerAvatarPrefab;
        [SerializeField] private Vector3 _playerSpawnOrigin = Vector3.zero;
        [SerializeField] private float _playerSpawnSpacing = 1.5f;

        private bool _isStarting;
        private NetworkRunner _runner;

        public async void StartSharedTestSession()
        {
            if (_isStarting || FindFirstObjectByType<NetworkRunner>() != null)
            {
                return;
            }

            int targetSceneBuildIndex = GetBuildIndexBySceneName(_targetSceneName);
            if (targetSceneBuildIndex < 0 || !_playerAvatarPrefab.IsValid)
            {
                return;
            }

            Application.runInBackground = true;
            _isStarting = true;

            GameObject runnerObject = new GameObject("Photon Fusion Test Runner (Shared)");
            DontDestroyOnLoad(runnerObject);

            _runner = runnerObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = true;

            runnerObject.AddComponent<NetworkAvatarInputProvider>();

            TestSharedAvatarSpawner avatarSpawner = runnerObject.AddComponent<TestSharedAvatarSpawner>();
            avatarSpawner.Initialize(_playerAvatarPrefab, _playerSpawnOrigin, _playerSpawnSpacing);

            NetworkSceneManagerDefault sceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>();

            StartGameResult result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = _sessionName,
                Scene = SceneRef.FromIndex(targetSceneBuildIndex),
                SceneManager = sceneManager,
                PlayerCount = _maxPlayers,
                IsOpen = true,
                IsVisible = true
            });

            if (result.Ok)
            {
                return;
            }

            Destroy(runnerObject);
            _runner = null;
            _isStarting = false;
        }

        private static int GetBuildIndexBySceneName(string sceneName)
        {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
                string buildSceneName = Path.GetFileNameWithoutExtension(scenePath);

                if (buildSceneName == sceneName)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    internal sealed class TestSharedAvatarSpawner : NetworkRunnerCallbacksBehaviour
    {
        private NetworkPrefabRef _avatarPrefab;
        private Vector3 _spawnOrigin;
        private float _spawnSpacing;
        private NetworkObject _localAvatar;
        private bool _sceneLoaded;

        public void Initialize(NetworkPrefabRef avatarPrefab, Vector3 spawnOrigin, float spawnSpacing)
        {
            _avatarPrefab = avatarPrefab;
            _spawnOrigin = spawnOrigin;
            _spawnSpacing = spawnSpacing;
        }

        public override void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (player == runner.LocalPlayer && _sceneLoaded)
            {
                SpawnLocalAvatar(runner);
            }
        }

        public override void OnSceneLoadStart(NetworkRunner runner)
        {
            _sceneLoaded = false;
            _localAvatar = null;
        }

        public override void OnSceneLoadDone(NetworkRunner runner)
        {
            _sceneLoaded = true;
            SpawnLocalAvatar(runner);
        }

        private void SpawnLocalAvatar(NetworkRunner runner)
        {
            if (_localAvatar != null || runner.GetPlayerObject(runner.LocalPlayer) != null)
            {
                return;
            }

            Vector3 spawnPosition = GetSpawnPosition(runner, runner.LocalPlayer);
            _localAvatar = runner.Spawn(
                prefabRef: _avatarPrefab,
                position: spawnPosition,
                rotation: Quaternion.identity,
                inputAuthority: runner.LocalPlayer);

            if (_localAvatar != null)
            {
                runner.SetPlayerObject(runner.LocalPlayer, _localAvatar);
            }
        }

        private Vector3 GetSpawnPosition(NetworkRunner runner, PlayerRef player)
        {
            int spawnIndex = 0;

            foreach (PlayerRef activePlayer in runner.ActivePlayers)
            {
                if (activePlayer == player)
                {
                    break;
                }

                spawnIndex++;
            }

            return _spawnOrigin + Vector3.right * (_spawnSpacing * spawnIndex);
        }
    }
}
```

## Self Review

- `NetworkAvatarSpawner`를 그대로 쓰지 않은 것은 의도적이다. 해당 클래스는 `runner.IsServer` 분기 때문에 Shared 모드에서 spawn 경로가 막힌다.
- `OnPlayerJoined`에서 바로 spawn하지 않고 `OnSceneLoadDone` 이후 spawn하는 것은 의도적이다. StartGame 직후 join 콜백이 먼저 오면 부트스트랩 씬 기준으로 avatar가 만들어질 수 있다.
- 각 클라이언트가 자기 avatar만 spawn한다. Shared 모드에서는 이 avatar의 StateAuthority가 spawn한 클라이언트가 되므로 소유 구조가 맞다.
- `SetPlayerObject`는 각 클라이언트가 자기 `Runner.LocalPlayer`에 대해서만 호출한다. Shared 모드 계획 문서의 방향과 맞다.
- `_playerAvatarPrefab.IsValid`가 false면 시작하지 않는다. 소화기 권한 테스트만 보면 Runner만 있어도 일부 확인 가능하지만, 이번 요구사항에 `NetworkAvatarSpawn`이 포함되어 있으므로 avatar prefab 누락은 실패로 보는 편이 맞다.
- `inputAuthority: runner.LocalPlayer`는 현재 `NetworkPlayerAvatar` 코드와 호환하기 위한 임시 선택이다. Shared 모드 최종 구조에서는 `NetworkPlayerAvatar` 자체를 StateAuthority 기반으로 정리한 뒤 제거 여부를 다시 판단해야 한다.
