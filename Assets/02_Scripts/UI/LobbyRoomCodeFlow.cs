using FireLink119.Network;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Samples.SpatialKeyboard;

namespace FireLink119.UI
{
    public class LobbyRoomCodeFlow : MonoBehaviour
    {
        [SerializeField] private string _hostRoomCodePadName = "Host Room Num";
        [SerializeField] private string _clientRoomCodePadName = "Client Room Num";
        [SerializeField] private int _maxDigits = 4;

        private GameObject _hostRoomCodePad;
        private GameObject _clientRoomCodePad;
        private GameObject _activeRoomCodePad;
        private TMP_InputField _activeInputField;
        private XRKeyboard _activeKeyboard;
        private XRKeyboardDisplay _activeKeyboardDisplay;
        private FusionRoomConnector _connector;
        private LobbyRoomRole _activeRole;
        private bool _hasActiveRole;
        private bool _isSanitizingInput;
        private int _currentCodeLength;
        private int _codeLengthBeforeLastChange;

        private void Awake()
        {
            // 넘패드는 로비 시작 시 숨긴 상태로 두고, Host/Client 버튼을 선택했을 때만 해당 패드를 활성화한다.
            _hostRoomCodePad = FindRoomCodePad(_hostRoomCodePadName, LobbyRoomRole.Host);
            _clientRoomCodePad = FindRoomCodePad(_clientRoomCodePadName, LobbyRoomRole.Client);
            _connector = ResolveConnector();

            SetPadActive(_hostRoomCodePad, false);
            SetPadActive(_clientRoomCodePad, false);
        }

        private void OnDisable()
        {
            // 씬 전환 또는 비활성화 시 입력 필드/키보드 이벤트가 남아 다음 선택에서 중복 호출되지 않게 정리한다.
            ClearActivePadBindings();
        }

        public void ShowRoomCodePad(LobbyRoomRole role)
        {
            // 선택한 역할에 맞는 패드만 열고, 반대쪽 패드는 반드시 닫아 로비에서 한 번에 하나의 입력 흐름만 유지한다.
            GameObject targetPad = role == LobbyRoomRole.Host ? _hostRoomCodePad : _clientRoomCodePad;
            if (targetPad == null)
            {
                string roleName = role == LobbyRoomRole.Host ? "Host" : "Client";
                Debug.LogWarning($"[LobbyRoomCodeFlow] {roleName} room code pad not found.");
                return;
            }

            ClearActivePadBindings();
            SetPadActive(_hostRoomCodePad, false);
            SetPadActive(_clientRoomCodePad, false);

            _activeRole = role;
            _hasActiveRole = true;
            _activeRoomCodePad = targetPad;
            SetPadActive(_activeRoomCodePad, true);

            ConfigureActivePad();
        }

        private FusionRoomConnector ResolveConnector()
        {
            // 실제 네트워크 시작 설정은 같은 오브젝트의 FusionRoomConnector에 모아 둔다.
            FusionRoomConnector connector = GetComponent<FusionRoomConnector>();
            if (connector == null)
            {
                Debug.LogError("[LobbyRoomCodeFlow] FusionRoomConnector is required on the same GameObject.");
            }

            return connector;
        }

        private void ConfigureActivePad()
        {
            // XRI Spatial Keyboard 예시 프리팹은 Display와 Keyboard가 분리되어 있어, 활성화된 패드 기준으로 다시 연결한다.
            _activeInputField = _activeRoomCodePad.GetComponentInChildren<TMP_InputField>(true);
            if (_activeInputField == null)
            {
                Debug.LogWarning($"[LobbyRoomCodeFlow] TMP_InputField not found under {_activeRoomCodePad.name}.");
                return;
            }

            _activeInputField.characterLimit = _maxDigits;
            _activeInputField.text = string.Empty;
            _activeInputField.onValueChanged.AddListener(OnRoomCodeChanged);
            _currentCodeLength = 0;
            _codeLengthBeforeLastChange = 0;

            _activeKeyboardDisplay = _activeRoomCodePad.GetComponentInChildren<XRKeyboardDisplay>(true);
            _activeKeyboard = ResolveKeyboard(_activeRoomCodePad, _activeKeyboardDisplay);
            if (_activeKeyboardDisplay != null)
            {
                // 샘플 키보드의 입력/표시 동작은 유지하고, 여기서는 4자리 제한과 제출 처리만 추가한다.
                _activeKeyboardDisplay.inputField = _activeInputField;
                if (_activeKeyboard != null)
                {
                    _activeKeyboardDisplay.useSceneKeyboard = true;
                    _activeKeyboardDisplay.keyboard = _activeKeyboard;
                }

                _activeKeyboardDisplay.monitorInputFieldCharacterLimit = true;
                _activeKeyboardDisplay.clearTextOnSubmit = false;
                _activeKeyboardDisplay.onTextSubmitted.AddListener(OnRoomCodeSubmitted);
            }
            else
            {
                Debug.LogWarning($"[LobbyRoomCodeFlow] XRKeyboardDisplay not found under {_activeRoomCodePad.name}.");
            }

            if (_activeKeyboard != null)
            {
                _activeKeyboard.onKeyPressed.AddListener(OnKeyboardKeyPressed);
            }
            else
            {
                Debug.LogWarning($"[LobbyRoomCodeFlow] XRKeyboard not found under {_activeRoomCodePad.name}.");
            }
        }

        private void ClearActivePadBindings()
        {
            // 이전 패드의 이벤트가 살아 있으면 제출/백스페이스 처리가 여러 번 실행되므로 현재 연결만 명시적으로 해제한다.
            if (_activeInputField != null)
            {
                _activeInputField.onValueChanged.RemoveListener(OnRoomCodeChanged);
            }

            if (_activeKeyboardDisplay != null)
            {
                _activeKeyboardDisplay.onTextSubmitted.RemoveListener(OnRoomCodeSubmitted);
            }

            if (_activeKeyboard != null)
            {
                _activeKeyboard.onKeyPressed.RemoveListener(OnKeyboardKeyPressed);
            }

            _activeInputField = null;
            _activeKeyboard = null;
            _activeKeyboardDisplay = null;
            _activeRoomCodePad = null;
            _hasActiveRole = false;
        }

        private void OnRoomCodeChanged(string value)
        {
            // 붙여넣기나 키보드 설정 차이로 숫자가 아닌 값이 들어와도 최종 방 코드는 4자리 숫자로만 유지한다.
            if (_isSanitizingInput)
            {
                return;
            }

            _codeLengthBeforeLastChange = _currentCodeLength;
            string sanitizedCode = SanitizeRoomCode(value);
            _currentCodeLength = sanitizedCode.Length;

            if (value == sanitizedCode)
            {
                return;
            }

            _isSanitizingInput = true;
            _activeInputField.text = sanitizedCode;
            _isSanitizingInput = false;
        }

        private void OnRoomCodeSubmitted(string submittedCode)
        {
            // Enter는 방 생성/입장 확정 액션이므로, 정확히 4자리 숫자가 완성된 경우에만 Fusion 연결로 넘긴다.
            if (!_hasActiveRole)
            {
                return;
            }

            string roomCode = SanitizeRoomCode(submittedCode);
            if (roomCode.Length != _maxDigits)
            {
                Debug.LogWarning($"[LobbyRoomCodeFlow] Room code must be {_maxDigits} digits.");
                return;
            }

            if (_connector == null)
            {
                Debug.LogError("[LobbyRoomCodeFlow] Cannot start room because FusionRoomConnector is not assigned.");
                return;
            }

            _connector.StartRoom(_activeRole, roomCode);
        }

        private void OnKeyboardKeyPressed(KeyboardKeyEventArgs args)
        {
            // 백스페이스는 숫자 삭제와 입력 취소 UX를 함께 담당하므로 일반 숫자 키와 분리해서 처리한다.
            if (args == null || !IsBackspaceKey(args.key))
            {
                return;
            }

            HandleBackspaceAfterKeyPress();
        }

        private void HandleBackspaceAfterKeyPress()
        {
            // 마지막 숫자를 지운 직후에는 패드를 유지하고, 이미 빈 상태에서 한 번 더 누르면 역할 선택 상태로 돌아간다.
            if (_activeInputField == null || !string.IsNullOrEmpty(_activeInputField.text))
            {
                return;
            }

            if (_codeLengthBeforeLastChange > 0)
            {
                _codeLengthBeforeLastChange = 0;
                _currentCodeLength = 0;
                return;
            }

            HideActivePad();
        }

        private void HideActivePad()
        {
            // 취소 시에는 현재 패드만 닫고 입력 상태를 초기화해 Host/Client 버튼을 다시 선택할 수 있게 한다.
            GameObject padToHide = _activeRoomCodePad;
            if (_activeInputField != null)
            {
                _activeInputField.text = string.Empty;
            }

            ClearActivePadBindings();
            SetPadActive(padToHide, false);
        }

        private XRKeyboard ResolveKeyboard(GameObject roomCodePad, XRKeyboardDisplay keyboardDisplay)
        {
            // 커스텀한 넘패드 구조가 샘플 프리팹과 조금 달라도 Display, 자식, 부모 순으로 Keyboard를 찾아 연결한다.
            if (keyboardDisplay != null && keyboardDisplay.keyboard != null)
            {
                return keyboardDisplay.keyboard;
            }

            XRKeyboard childKeyboard = roomCodePad.GetComponentInChildren<XRKeyboard>(true);
            if (childKeyboard != null)
            {
                return childKeyboard;
            }

            Transform current = roomCodePad.transform.parent;
            while (current != null)
            {
                XRKeyboard parentKeyboard = current.GetComponent<XRKeyboard>();
                if (parentKeyboard != null)
                {
                    return parentKeyboard;
                }

                current = current.parent;
            }

            return null;
        }

        private GameObject FindRoomCodePad(string configuredName, LobbyRoomRole role)
        {
            // 비활성 오브젝트는 GameObject.Find로 찾을 수 없으므로 Resources 검색 기반 fallback을 사용한다.
            GameObject exactMatch = FindSceneObjectByName(configuredName);
            if (exactMatch != null)
            {
                return exactMatch;
            }

            return FindSceneObjectByRole(role);
        }

        private GameObject FindSceneObjectByName(string objectName)
        {
            // 로비 시작 시 Host/Client 패드는 비활성 상태일 수 있으므로, 현재 씬에 속한 Transform 전체를 검색한다.
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < transforms.Length; i++)
            {
                GameObject candidate = transforms[i].gameObject;
                if (!candidate.scene.IsValid())
                {
                    continue;
                }

                if (candidate.name == objectName)
                {
                    return candidate;
                }
            }

            return null;
        }

        private GameObject FindSceneObjectByRole(LobbyRoomRole role)
        {
            // Inspector 이름이 조금 달라도 Host/Client와 Num 단어가 포함된 패드를 찾아 기본 동작을 유지한다.
            string roleName = role == LobbyRoomRole.Host ? "Host" : "Client";
            Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();

            for (int i = 0; i < transforms.Length; i++)
            {
                GameObject candidate = transforms[i].gameObject;
                if (!candidate.scene.IsValid())
                {
                    continue;
                }

                string candidateName = candidate.name;
                bool matchesRole = candidateName.Contains(roleName);
                bool looksLikeNumberPad = candidateName.Contains("Room Num") || candidateName.Contains("Num");

                if (matchesRole && looksLikeNumberPad)
                {
                    return candidate;
                }
            }

            return null;
        }

        private string SanitizeRoomCode(string value)
        {
            // 방 코드는 Photon SessionName으로 쓰이므로 숫자만 남기고 최대 자리 수를 넘지 않게 제한한다.
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            int length = Mathf.Min(value.Length, _maxDigits);
            char[] digits = new char[_maxDigits];
            int digitCount = 0;

            for (int i = 0; i < value.Length && digitCount < length; i++)
            {
                if (!char.IsDigit(value[i]))
                {
                    continue;
                }

                digits[digitCount] = value[i];
                digitCount++;
            }

            return new string(digits, 0, digitCount);
        }

        private bool IsBackspaceKey(XRKeyboardKey key)
        {
            // XRI 샘플 키보드 버전에 따라 Backspace 표현이 다를 수 있어 KeyCode와 문자 값을 모두 허용한다.
            if (key == null)
            {
                return false;
            }

            return key.keyCode == KeyCode.Backspace || key.character == "\b";
        }

        private void SetPadActive(GameObject pad, bool isActive)
        {
            // 패드가 아직 없거나 이름이 다른 상황에서도 로비 버튼 흐름이 null 예외로 멈추지 않게 한다.
            if (pad != null)
            {
                pad.SetActive(isActive);
            }
        }
    }
}
