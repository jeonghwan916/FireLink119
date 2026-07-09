using System.IO;
using FireLink119.Player;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FireLink119.Network
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Collider))]
    public class NetworkEndSceneTrigger : NetworkBehaviour
    {
        [SerializeField] private string _endingSceneName = "EndingScene";
        [SerializeField] private string _playerTag = "Player";
        [SerializeField] private bool _allowPlayerTagContact = true;
        [SerializeField] private bool _allowOfflineFallback = true;

        private bool _hasRequestedSceneLoad;

        private void OnTriggerEnter(Collider other)
        {
            if (_hasRequestedSceneLoad || !IsPlayerContact(other))
            {
                return;
            }

            if (Runner == null)
            {
                TryLoadOffline();
                return;
            }

            if (!HasStateAuthority)
            {
                return;
            }

            TryLoadWithRunner();
        }

        private void TryLoadWithRunner()
        {
            int endingSceneBuildIndex = GetBuildIndexBySceneName(_endingSceneName);
            if (endingSceneBuildIndex < 0)
            {
                Debug.LogError($"[NetworkEndSceneTrigger] Ending scene is not registered in Build Settings: {_endingSceneName}", this);
                return;
            }

            _hasRequestedSceneLoad = true;
            Runner.LoadScene(SceneRef.FromIndex(endingSceneBuildIndex), LoadSceneMode.Single);
        }

        private void TryLoadOffline()
        {
            if (!_allowOfflineFallback)
            {
                Debug.LogWarning("[NetworkEndSceneTrigger] NetworkRunner is required to load the ending scene.", this);
                return;
            }

            if (GetBuildIndexBySceneName(_endingSceneName) < 0)
            {
                Debug.LogError($"[NetworkEndSceneTrigger] Ending scene is not registered in Build Settings: {_endingSceneName}", this);
                return;
            }

            _hasRequestedSceneLoad = true;
            SceneManager.LoadScene(_endingSceneName, LoadSceneMode.Single);
        }

        private bool IsPlayerContact(Collider other)
        {
            if (other == null)
            {
                return false;
            }

            if (other.GetComponentInParent<PlayerIdentifier>() != null)
            {
                return true;
            }

            return _allowPlayerTagContact && HasTagInParents(other.transform, _playerTag);
        }

        private static bool HasTagInParents(Transform source, string tagName)
        {
            for (Transform current = source; current != null; current = current.parent)
            {
                if (current.CompareTag(tagName))
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetBuildIndexBySceneName(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return -1;
            }

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
