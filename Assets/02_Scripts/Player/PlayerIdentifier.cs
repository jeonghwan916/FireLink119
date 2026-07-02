using UnityEngine;

namespace FireLink119.Player
{
    public class PlayerIdentifier : MonoBehaviour
    {
        [SerializeField] private PlayerType _playerType = PlayerType.Player1;

        public PlayerType PlayerType => _playerType;

        public void SetPlayerType(PlayerType playerType)
        {
            _playerType = playerType;
        }
    }
}
