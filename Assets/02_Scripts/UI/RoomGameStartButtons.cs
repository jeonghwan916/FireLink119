using System.Collections;
using FireLink119.Network;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace FireLink119.UI
{
    public class RoomGameStartButtons : MonoBehaviour
    {
        [SerializeField] private string _roomSceneNameContains = "RoomScene";
        [SerializeField] private string _hostButtonName = "Player Button 1";
        [SerializeField] private string _clientButtonName = "Player Button 2";

        private XRSimpleInteractable _hostButton;
        private XRSimpleInteractable _clientButton;
        private bool _isListening;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeForLoadedScenes()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            TryCreateForScene(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
        {
            TryCreateForScene(scene);
        }

        private static void TryCreateForScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || !scene.name.Contains("RoomScene"))
            {
                return;
            }

            if (FindFirstObjectByType<RoomGameStartButtons>() != null)
            {
                return;
            }

            GameObject managerObject = new GameObject("Room Game Start Buttons");
            managerObject.AddComponent<RoomGameStartButtons>();
        }

        private IEnumerator Start()
        {
            yield return null;

            if (!SceneManager.GetActiveScene().name.Contains(_roomSceneNameContains))
            {
                enabled = false;
                yield break;
            }

            ResolveButtons();
            RegisterButtonListeners();
        }

        private void OnDisable()
        {
            UnregisterButtonListeners();
        }

        private void ResolveButtons()
        {
            _hostButton = FindButton(_hostButtonName);
            _clientButton = FindButton(_clientButtonName);
        }

        private XRSimpleInteractable FindButton(string buttonName)
        {
            GameObject buttonObject = GameObject.Find(buttonName);
            if (buttonObject == null)
            {
                Debug.LogWarning($"[RoomGameStartButtons] Button not found: {buttonName}");
                return null;
            }

            if (!buttonObject.TryGetComponent(out XRSimpleInteractable interactable))
            {
                Debug.LogWarning($"[RoomGameStartButtons] XRSimpleInteractable not found: {buttonName}");
                return null;
            }

            return interactable;
        }

        private void RegisterButtonListeners()
        {
            if (_isListening)
            {
                return;
            }

            if (_hostButton != null)
            {
                _hostButton.selectEntered.AddListener(OnHostButtonSelected);
            }

            if (_clientButton != null)
            {
                _clientButton.selectEntered.AddListener(OnClientButtonSelected);
            }

            _isListening = true;
        }

        private void UnregisterButtonListeners()
        {
            if (!_isListening)
            {
                return;
            }

            if (_hostButton != null)
            {
                _hostButton.selectEntered.RemoveListener(OnHostButtonSelected);
            }

            if (_clientButton != null)
            {
                _clientButton.selectEntered.RemoveListener(OnClientButtonSelected);
            }

            _isListening = false;
        }

        private void OnHostButtonSelected(SelectEnterEventArgs args)
        {
            RequestRoomStart(LobbyRoomRole.Host);
        }

        private void OnClientButtonSelected(SelectEnterEventArgs args)
        {
            RequestRoomStart(LobbyRoomRole.Client);
        }

        private void RequestRoomStart(LobbyRoomRole pressedButtonRole)
        {
            NetworkRunner runner = FindFirstObjectByType<NetworkRunner>();
            if (runner == null)
            {
                Debug.LogWarning("[RoomGameStartButtons] NetworkRunner was not found.");
                return;
            }

            LobbyRoomRole localRole = runner.IsServer ? LobbyRoomRole.Host : LobbyRoomRole.Client;
            if (pressedButtonRole != localRole)
            {
                Debug.LogWarning($"[RoomGameStartButtons] Ignored button for another role. Local: {localRole}, Button: {pressedButtonRole}");
                return;
            }

            NetworkObject playerObject = runner.GetPlayerObject(runner.LocalPlayer);
            if (playerObject == null)
            {
                Debug.LogWarning("[RoomGameStartButtons] Local player NetworkObject was not found yet.");
                return;
            }

            if (!playerObject.TryGetComponent(out NetworkPlayerAvatar avatar))
            {
                Debug.LogWarning("[RoomGameStartButtons] NetworkPlayerAvatar was not found on the local player object.");
                return;
            }

            avatar.RequestRoomStartButtonPress(pressedButtonRole);
        }
    }
}
