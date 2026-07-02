using System.IO;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FireLink119.Network
{
    public class FusionRoomConnector : MonoBehaviour
    {
        [SerializeField] private string _roomSceneName = "RoomScene";
        [SerializeField] private int _maxPlayers = 2;
        [SerializeField] private NetworkPrefabRef _playerAvatarPrefab;
        [SerializeField] private Vector3 _playerSpawnOrigin = Vector3.zero;
        [SerializeField] private float _playerSpawnSpacing = 1.5f;

        private bool _isStarting;
        private NetworkRunner _runner;

        public async void StartRoom(LobbyRoomRole role, string roomCode)
        {
            // 방 생성/입장은 비동기로 진행되므로 같은 버튼이 연속 선택되어 Runner가 중복 생성되는 것을 막는다.
            if (_isStarting)
            {
                return;
            }

            if (!IsValidRoomCode(roomCode))
            {
                Debug.LogWarning("[FusionRoomConnector] Room code must be exactly 4 digits.");
                return;
            }

            int roomSceneBuildIndex = GetBuildIndexBySceneName(_roomSceneName);
            if (roomSceneBuildIndex < 0)
            {
                Debug.LogError($"[FusionRoomConnector] Room scene is not registered in Build Settings: {_roomSceneName}");
                return;
            }

            GameMode gameMode = role == LobbyRoomRole.Host ? GameMode.Host : GameMode.Client;
            _isStarting = true;

            GameObject runnerObject = new GameObject($"Photon Fusion Runner ({gameMode})");
            DontDestroyOnLoad(runnerObject);

            _runner = runnerObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = true;

            // Runner와 같은 오브젝트에 네트워크 입력 수집과 아바타 스폰 콜백을 붙여 Fusion 생명주기와 함께 관리한다.
            runnerObject.AddComponent<NetworkAvatarInputProvider>();
            NetworkAvatarSpawner avatarSpawner = runnerObject.AddComponent<NetworkAvatarSpawner>();
            avatarSpawner.Initialize(_playerAvatarPrefab, _playerSpawnOrigin, _playerSpawnSpacing);

            NetworkSceneManagerDefault sceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>();

            // 로비에서 입력한 4자리 코드를 Fusion SessionName으로 사용해 Host/Client가 같은 방을 찾도록 한다.
            StartGameResult result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = gameMode,
                SessionName = roomCode,
                Scene = SceneRef.FromIndex(roomSceneBuildIndex),
                SceneManager = sceneManager,
                PlayerCount = _maxPlayers,
                IsOpen = true,
                IsVisible = true,
                EnableClientSessionCreation = false
            });

            if (!result.Ok)
            {
                Debug.LogWarning($"[FusionRoomConnector] Photon room start failed. Mode: {gameMode}, Code: {roomCode}, Reason: {result.ShutdownReason}");
                Destroy(runnerObject);
                _runner = null;
                _isStarting = false;
                return;
            }

            Debug.Log($"[FusionRoomConnector] Photon room started. Mode: {gameMode}, Code: {roomCode}");
        }

        private bool IsValidRoomCode(string roomCode)
        {
            // SessionName으로 전달하기 전에 로비 규칙을 한 번 더 검증해 잘못된 방 생성/입장을 차단한다.
            if (string.IsNullOrWhiteSpace(roomCode) || roomCode.Length != 4)
            {
                return false;
            }

            for (int i = 0; i < roomCode.Length; i++)
            {
                if (!char.IsDigit(roomCode[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private int GetBuildIndexBySceneName(string sceneName)
        {
            // Inspector에서는 씬 이름으로 관리하고, Fusion StartGame에는 Build Settings 인덱스 기반 SceneRef를 넘긴다.
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
}
