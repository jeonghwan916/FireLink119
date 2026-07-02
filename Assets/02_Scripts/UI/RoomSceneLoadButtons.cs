using FireLink119.Network;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace FireLink119.UI
{
    [RequireComponent(typeof(LobbyRoomCodeFlow))]
    [RequireComponent(typeof(FusionRoomConnector))]
    public class RoomSceneLoadButtons : MonoBehaviour
    {
        // 기존 Push 버튼 프리팹을 복사한 구조라 Inspector 직접 참조 대신 이름으로 찾아 기존 UI 연결을 덜 건드린다.
        [SerializeField] private string _hostButtonName = "Host Button";
        [SerializeField] private string _clientButtonName = "Client Button";

        private XRSimpleInteractable _hostButton;
        private XRSimpleInteractable _clientButton;
        private LobbyRoomCodeFlow _roomCodeFlow;

        private void Awake()
        {
            _hostButton = FindButton(_hostButtonName);
            _clientButton = FindButton(_clientButtonName);

            // 이 스크립트는 선택 이벤트만 담당하고, 코드 입력/방 시작 흐름은 LobbyRoomCodeFlow에 분리한다.
            _roomCodeFlow = GetComponent<LobbyRoomCodeFlow>();
            if (_roomCodeFlow == null)
            {
                Debug.LogError("[RoomSceneLoadButtons] LobbyRoomCodeFlow is required on the same GameObject.");
            }
        }

        private void OnEnable()
        {
            // XR 버튼은 일반 Unity UI Button.onClick이 아니라 XRI selectEntered 이벤트로 선택 입력을 받는다.
            if (_hostButton != null)
            {
                _hostButton.selectEntered.AddListener(OnHostButtonSelected);
            }

            if (_clientButton != null)
            {
                _clientButton.selectEntered.AddListener(OnClientButtonSelected);
            }
        }

        private void OnDisable()
        {
            // 씬 전환이나 오브젝트 비활성화 후 다시 켜질 때 같은 이벤트가 중복 등록되지 않도록 해제한다.
            if (_hostButton != null)
            {
                _hostButton.selectEntered.RemoveListener(OnHostButtonSelected);
            }

            if (_clientButton != null)
            {
                _clientButton.selectEntered.RemoveListener(OnClientButtonSelected);
            }
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

        private void OnHostButtonSelected(SelectEnterEventArgs args)
        {
            // Host 버튼을 누르면 곧바로 방을 만들지 않고, 먼저 Host용 4자리 코드 입력 패드를 연다.
            if (_roomCodeFlow == null)
            {
                return;
            }

            _roomCodeFlow.ShowRoomCodePad(LobbyRoomRole.Host);
        }

        private void OnClientButtonSelected(SelectEnterEventArgs args)
        {
            // Client도 같은 코드 입력 흐름을 사용하되, 제출 시 Client 모드로 방에 입장한다.
            if (_roomCodeFlow == null)
            {
                return;
            }

            _roomCodeFlow.ShowRoomCodePad(LobbyRoomRole.Client);
        }
    }
}
