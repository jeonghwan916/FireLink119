using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace FireLink119.Network
{
    public class NetworkAvatarSpawner : NetworkRunnerCallbacksBehaviour
    {
        private readonly Dictionary<PlayerRef, NetworkObject> _spawnedAvatars = new Dictionary<PlayerRef, NetworkObject>();

        private NetworkPrefabRef _avatarPrefab;
        private Vector3 _spawnOrigin;
        private float _spawnSpacing;

        public void Initialize(NetworkPrefabRef avatarPrefab, Vector3 spawnOrigin, float spawnSpacing)
        {
            // FusionRoomConnector가 런타임에 Runner를 만들기 때문에 Inspector 대신 초기화 메서드로 스폰 설정을 전달한다.
            _avatarPrefab = avatarPrefab;
            _spawnOrigin = spawnOrigin;
            _spawnSpacing = spawnSpacing;
        }

        public override void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (!runner.IsServer)
            {
                return;
            }

            SpawnAvatarForPlayer(runner, player);
        }

        public override void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (!_spawnedAvatars.TryGetValue(player, out NetworkObject avatarObject))
            {
                return;
            }

            if (avatarObject != null)
            {
                runner.Despawn(avatarObject);
            }

            _spawnedAvatars.Remove(player);
        }

        public override void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            _spawnedAvatars.Clear();
        }

        public override void OnSceneLoadStart(NetworkRunner runner)
        {
            if (!runner.IsServer)
            {
                return;
            }

            // LoadSceneMode.Single 전환에서는 이전 씬의 런타임 스폰 오브젝트가 정리된다.
            // 플레이어는 방에 남아 있으므로, 씬 로드 완료 후 다시 스폰할 수 있게 로컬 참조만 비운다.
            _spawnedAvatars.Clear();
        }

        public override void OnSceneLoadDone(NetworkRunner runner)
        {
            if (!runner.IsServer)
            {
                return;
            }

            foreach (PlayerRef player in runner.ActivePlayers)
            {
                SpawnAvatarForPlayer(runner, player);
            }
        }

        private void SpawnAvatarForPlayer(NetworkRunner runner, PlayerRef player)
        {
            if (_spawnedAvatars.TryGetValue(player, out NetworkObject existingAvatar) && existingAvatar != null)
            {
                runner.SetPlayerObject(player, existingAvatar);
                return;
            }

            if (!_avatarPrefab.IsValid)
            {
                Debug.LogWarning("[NetworkAvatarSpawner] Network avatar prefab is not assigned.");
                return;
            }

            Vector3 spawnPosition = GetSpawnPosition();
            NetworkObject avatarObject = runner.Spawn(
                prefabRef: _avatarPrefab,
                position: spawnPosition,
                rotation: Quaternion.identity,
                inputAuthority: player);

            _spawnedAvatars[player] = avatarObject;
            runner.SetPlayerObject(player, avatarObject);
        }

        private Vector3 GetSpawnPosition()
        {
            // 현재 스폰된 플레이어 수 기준으로 간격을 두어 같은 좌표에 겹치지 않게 배치한다.
            int spawnIndex = _spawnedAvatars.Count;
            return _spawnOrigin + Vector3.right * (_spawnSpacing * spawnIndex);
        }
    }
}
