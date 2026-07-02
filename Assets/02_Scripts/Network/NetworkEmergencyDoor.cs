using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace FireLink119.Network
{
    public class NetworkEmergencyDoor : MonoBehaviour
    {
        private enum DoorRotationAxis
        {
            X,
            Y,
            Z
        }

        private const string DefaultDoorRootName = "EmergencyDoorRoot";
        private const float SmallDistanceThreshold = 0.0001f;

        private static readonly Dictionary<int, NetworkEmergencyDoor> Doors = new Dictionary<int, NetworkEmergencyDoor>();

        [SerializeField] private int _doorId;
        [SerializeField] private DoorRotationAxis _rotationAxis = DoorRotationAxis.Z;
        [SerializeField] private float _openRotationDirection = -1f;
        [SerializeField] private float _maxOpenAngle = 90f;
        [SerializeField] private float _pushSensitivity = 1f;
        [SerializeField] private float _maxDegreesPerFrame = 5f;
        [SerializeField] private float _minPushDegrees = 0.05f;
        [SerializeField] private bool _allowLocalFallbackWithoutRunner = true;

        private XRSimpleInteractable _interactable;
        private Transform _activeInteractorTransform;
        private Vector3 _previousInteractorPosition;
        private Quaternion _closedLocalRotation;
        private float _currentOpenAngle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeRuntimeDoorBinding()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TryInstallDoorInActiveScene();
        }

        public static bool TryApplyPushAsStateAuthority(int doorId, float openAngleDelta, out float openAngle)
        {
            openAngle = 0f;

            if (!Doors.TryGetValue(doorId, out NetworkEmergencyDoor door) || door == null)
            {
                return false;
            }

            door.ApplyPush(openAngleDelta);
            openAngle = door._currentOpenAngle;
            return true;
        }

        public static void ApplyNetworkAngle(int doorId, float openAngle)
        {
            if (!Doors.TryGetValue(doorId, out NetworkEmergencyDoor door) || door == null)
            {
                return;
            }

            door.SetOpenAngle(openAngle);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryInstallDoorInActiveScene();
        }

        private static void TryInstallDoorInActiveScene()
        {
            GameObject doorRoot = GameObject.Find(DefaultDoorRootName);
            if (doorRoot == null)
            {
                return;
            }

            if (!doorRoot.TryGetComponent(out XRSimpleInteractable _))
            {
                doorRoot.AddComponent<XRSimpleInteractable>();
            }

            if (!doorRoot.TryGetComponent(out NetworkEmergencyDoor door))
            {
                doorRoot.AddComponent<NetworkEmergencyDoor>();
            }
        }

        private void Awake()
        {
            _closedLocalRotation = transform.localRotation;
            _interactable = GetComponent<XRSimpleInteractable>();
        }

        private void OnEnable()
        {
            Doors[_doorId] = this;

            if (_interactable == null)
            {
                _interactable = GetComponent<XRSimpleInteractable>();
            }

            if (_interactable == null)
            {
                Debug.LogWarning($"[NetworkEmergencyDoor] XRSimpleInteractable not found: {name}");
                return;
            }

            _interactable.selectEntered.AddListener(OnSelectEntered);
            _interactable.selectExited.AddListener(OnSelectExited);
        }

        private void OnDisable()
        {
            if (Doors.TryGetValue(_doorId, out NetworkEmergencyDoor door) && door == this)
            {
                Doors.Remove(_doorId);
            }

            if (_interactable != null)
            {
                _interactable.selectEntered.RemoveListener(OnSelectEntered);
                _interactable.selectExited.RemoveListener(OnSelectExited);
            }
        }

        private void Update()
        {
            if (_activeInteractorTransform == null)
            {
                return;
            }

            Vector3 currentInteractorPosition = _activeInteractorTransform.position;
            float openAngleDelta = CalculateOpenAngleDelta(_previousInteractorPosition, currentInteractorPosition);
            _previousInteractorPosition = currentInteractorPosition;

            if (Mathf.Abs(openAngleDelta) < _minPushDegrees)
            {
                return;
            }

            RequestDoorPush(openAngleDelta);
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (args.interactorObject == null)
            {
                return;
            }

            _activeInteractorTransform = args.interactorObject.transform;
            _previousInteractorPosition = _activeInteractorTransform.position;
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (args.interactorObject == null)
            {
                return;
            }

            if (_activeInteractorTransform == args.interactorObject.transform)
            {
                _activeInteractorTransform = null;
            }
        }

        private float CalculateOpenAngleDelta(Vector3 previousPosition, Vector3 currentPosition)
        {
            Vector3 worldAxis = GetWorldRotationAxis();
            Vector3 hingePosition = transform.position;
            Vector3 previousOffset = Vector3.ProjectOnPlane(previousPosition - hingePosition, worldAxis);
            Vector3 currentOffset = Vector3.ProjectOnPlane(currentPosition - hingePosition, worldAxis);

            if (previousOffset.sqrMagnitude < SmallDistanceThreshold || currentOffset.sqrMagnitude < SmallDistanceThreshold)
            {
                return 0f;
            }

            float signedAngle = Vector3.SignedAngle(previousOffset, currentOffset, worldAxis);
            float openAngleDelta = signedAngle * _openRotationDirection * _pushSensitivity;
            return Mathf.Clamp(openAngleDelta, -_maxDegreesPerFrame, _maxDegreesPerFrame);
        }

        private void RequestDoorPush(float openAngleDelta)
        {
            if (TryGetLocalNetworkAvatar(out NetworkPlayerAvatar localAvatar))
            {
                localAvatar.RequestEmergencyDoorPush(_doorId, openAngleDelta);
                return;
            }

            if (!_allowLocalFallbackWithoutRunner)
            {
                return;
            }

            ApplyPush(openAngleDelta);
        }

        private bool TryGetLocalNetworkAvatar(out NetworkPlayerAvatar localAvatar)
        {
            localAvatar = null;
            NetworkRunner runner = FindFirstObjectByType<NetworkRunner>();
            if (runner == null)
            {
                return false;
            }

            NetworkObject playerObject = runner.GetPlayerObject(runner.LocalPlayer);
            if (playerObject == null)
            {
                return false;
            }

            return playerObject.TryGetComponent(out localAvatar);
        }

        private void ApplyPush(float openAngleDelta)
        {
            SetOpenAngle(_currentOpenAngle + openAngleDelta);
        }

        private void SetOpenAngle(float openAngle)
        {
            _currentOpenAngle = Mathf.Clamp(openAngle, 0f, _maxOpenAngle);
            ApplyRotation();
        }

        private void ApplyRotation()
        {
            transform.localRotation = _closedLocalRotation * Quaternion.AngleAxis(_currentOpenAngle * _openRotationDirection, GetLocalRotationAxis());
        }

        private Vector3 GetWorldRotationAxis()
        {
            return transform.TransformDirection(GetLocalRotationAxis());
        }

        private Vector3 GetLocalRotationAxis()
        {
            switch (_rotationAxis)
            {
                case DoorRotationAxis.X:
                    return Vector3.right;
                case DoorRotationAxis.Y:
                    return Vector3.up;
                default:
                    return Vector3.forward;
            }
        }
    }
}
