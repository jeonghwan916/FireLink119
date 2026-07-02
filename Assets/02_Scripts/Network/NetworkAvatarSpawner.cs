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
            // FusionRoomConnector가 런타임 Runner를 만들 때 Inspector 값을 넘겨준다.
            // 스포너 자체는 씬에 미리 배치하지 않기 때문에 별도 초기화 메서드가 필요하다.
            _avatarPrefab = avatarPrefab;
            _spawnOrigin = spawnOrigin;
            _spawnSpacing = spawnSpacing;
        }

        public override void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            // Fusion Host 모드에서는 서버 권한을 가진 쪽만 Spawn해야 모든 클라이언트에 동일한 NetworkObject가 복제된다.
            if (!runner.IsServer || _spawnedAvatars.ContainsKey(player))
            {
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

        public override void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            // 나간 플레이어의 아바타를 명시적으로 제거해 방에 빈 캐릭터가 남지 않게 한다.
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
            // Runner 종료 시 Fusion이 객체 정리를 처리하므로, 여기서는 로컬 매핑만 비워 다음 세션과 섞이지 않게 한다.
            _spawnedAvatars.Clear();
        }

        private Vector3 GetSpawnPosition()
        {
            // 2인 테스트에서 두 아바타가 같은 좌표에 겹치지 않도록 접속 순서만큼 간단히 옆으로 띄운다.
            int spawnIndex = _spawnedAvatars.Count;
            return _spawnOrigin + Vector3.right * (_spawnSpacing * spawnIndex);
        }
    }
}
