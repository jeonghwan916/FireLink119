using System;
using System.IO;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

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
        
        [Header("Buttons")]
        [Tooltip("Delete later maybe")]
        [SerializeField] private string _buttonName = "Button";
        private XRSimpleInteractable _button;
        
        private void OnEnable()
        {
            _button = FindButton(_buttonName);
            _button.activated.AddListener(OnButtonActivated); // error part
        }

        private void OnDisable()
        {
            _button.activated.RemoveAllListeners();
        }

        private XRSimpleInteractable FindButton(string buttonName)
        {
            // 버튼 참조가 빠져 있어도 현재 로비 계층 구조와 이름이 맞으면 동작하게 하기 위한 자동 연결이다.
            GameObject buttonObject = GameObject.Find(buttonName);
            if (buttonObject == null)
            {
                Debug.LogWarning($"[RoomSceneLoadButtons] Button not found: {buttonName}");
                return null;
            }

            if (!buttonObject.TryGetComponent(out XRSimpleInteractable interactable))
            {
                Debug.LogWarning($"[RoomSceneLoadButtons] XRSimpleInteractable not found: {buttonName}");
                return null;
            }

            return interactable;
        }
        
        private void OnButtonActivated(ActivateEventArgs args)
        {
            StartSharedTestSession();
        }
        
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