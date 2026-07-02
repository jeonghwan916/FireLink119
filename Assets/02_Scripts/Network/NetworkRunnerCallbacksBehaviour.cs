using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

namespace FireLink119.Network
{
    public abstract class NetworkRunnerCallbacksBehaviour : MonoBehaviour, INetworkRunnerCallbacks
    {
        // Fusion의 INetworkRunnerCallbacks는 메서드 수가 많다.
        // 필요한 콜백만 상속 클래스에서 override할 수 있도록 기본 빈 구현을 제공한다.

        public virtual void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
        {
        }

        public virtual void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
        {
        }

        public virtual void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
        }

        public virtual void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
        }

        public virtual void OnInput(NetworkRunner runner, NetworkInput input)
        {
        }

        public virtual void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
        {
        }

        public virtual void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
        }

        public virtual void OnConnectedToServer(NetworkRunner runner)
        {
        }

        public virtual void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
        }

        public virtual void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
        {
        }

        public virtual void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
        }

        public virtual void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
        {
        }

        public virtual void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
        {
        }

        public virtual void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data)
        {
        }

        public virtual void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
        {
        }

        public virtual void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
        {
        }

        public virtual void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
        {
        }

        public virtual void OnSceneLoadDone(NetworkRunner runner)
        {
        }

        public virtual void OnSceneLoadStart(NetworkRunner runner)
        {
        }
    }
}
