using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FireLink119.Interaction
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TwoHandleCarryController : MonoBehaviour
    {
        private const int NoPlayerId = int.MinValue;

        [Header("Grab Zones")] [SerializeField]
        private XRBaseInteractable _leftGrabZone;

        [SerializeField] private XRBaseInteractable _rightGrabZone;

        [Header("Anchors")] [SerializeField] private Transform _leftAnchor;
        [SerializeField] private Transform _rightAnchor;

        [Header("Carry Rule")] [SerializeField]
        private bool _requireDifferentPlayers = true;

#if UNITY_EDITOR
        [Header("Solo Test")] [SerializeField] private bool _soloTestTreatHandlesAsDifferentPlayers;
#endif

        private Rigidbody _rigidbody;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.isKinematic = true;
        }

        private void FixedUpdate()
        {
            IXRSelectInteractor leftInteractor = GetFirstSelectingInteractor(_leftGrabZone);
            IXRSelectInteractor rightInteractor = GetFirstSelectingInteractor(_rightGrabZone);

            if (!CanCarry(leftInteractor, rightInteractor))
            {
                _rigidbody.isKinematic = false;
                return;
            }

            _rigidbody.isKinematic = true;

            Vector3 leftOffset = _leftAnchor.position - transform.position;
            Vector3 rightOffset = _rightAnchor.position - transform.position;

            Vector3 leftTarget = GetInteractorPosition(leftInteractor, _leftGrabZone) - leftOffset;
            Vector3 rightTarget = GetInteractorPosition(rightInteractor, _rightGrabZone) - rightOffset;

            Vector3 targetPosition = (leftTarget + rightTarget) * 0.5f;

            _rigidbody.MovePosition(targetPosition);
        }

        private bool CanCarry(IXRSelectInteractor leftInteractor, IXRSelectInteractor rightInteractor)
        {
            if (leftInteractor == null || rightInteractor == null)
            {
                return false;
            }

            if (ReferenceEquals(leftInteractor, rightInteractor))
            {
                return false;
            }

            if (!_requireDifferentPlayers)
            {
                return true;
            }

            int leftPlayerId = ResolvePlayerId(leftInteractor);
            int rightPlayerId = ResolvePlayerId(rightInteractor);

            return leftPlayerId != NoPlayerId &&
                   rightPlayerId != NoPlayerId &&
                   leftPlayerId != rightPlayerId;
        }

        private static IXRSelectInteractor GetFirstSelectingInteractor(XRBaseInteractable interactable)
        {
            if (interactable == null || interactable.interactorsSelecting.Count == 0)
            {
                return null;
            }

            return interactable.interactorsSelecting[0];
        }

        private static Vector3 GetInteractorPosition(IXRSelectInteractor interactor, XRBaseInteractable interactable)
        {
            Transform attachTransform = interactor.GetAttachTransform(interactable);
            return attachTransform != null ? attachTransform.position : interactor.transform.position;
        }

        private int ResolvePlayerId(IXRSelectInteractor interactor)
        {
#if UNITY_EDITOR
            if (_soloTestTreatHandlesAsDifferentPlayers)
            {
                return interactor.transform.GetInstanceID();
            }
#endif

            if (TryResolvePlayerIdFromHierarchy(interactor.transform, out int playerId))
            {
                return playerId;
            }

            Debug.LogWarning(
                $"[TwoHandleCarryController] Player id not found from interactor: {interactor.transform.name}", this);
            return NoPlayerId;
        }

        private static bool TryResolvePlayerIdFromHierarchy(Transform grabber, out int playerId)
        {
            for (Transform current = grabber; current != null; current = current.parent)
            {
                MonoBehaviour[] behaviours = current.GetComponents<MonoBehaviour>();

                foreach (MonoBehaviour behaviour in behaviours)
                {
                    if (behaviour == null)
                    {
                        continue;
                    }

                    if (TryReadIntMember(behaviour, "PlayerId", out playerId) ||
                        TryReadIntMember(behaviour, "OwnerPlayerId", out playerId) ||
                        TryReadIntMember(behaviour, "PlayerIndex", out playerId))
                    {
                        return true;
                    }

                    if (TryReadHashableMember(behaviour, "InputAuthority", out playerId) ||
                        TryReadHashableMember(behaviour, "OwnerClientId", out playerId))
                    {
                        return true;
                    }
                }
            }

            playerId = NoPlayerId;
            return false;
        }

        private static bool TryReadIntMember(MonoBehaviour behaviour, string memberName, out int value)
        {
            System.Type type = behaviour.GetType();

            System.Reflection.PropertyInfo property = type.GetProperty(memberName);
            if (property != null && property.PropertyType == typeof(int))
            {
                value = (int)property.GetValue(behaviour);
                return true;
            }

            System.Reflection.FieldInfo field = type.GetField(memberName);
            if (field != null && field.FieldType == typeof(int))
            {
                value = (int)field.GetValue(behaviour);
                return true;
            }

            value = NoPlayerId;
            return false;
        }

        private static bool TryReadHashableMember(MonoBehaviour behaviour, string memberName, out int value)
        {
            System.Type type = behaviour.GetType();

            System.Reflection.PropertyInfo property = type.GetProperty(memberName);
            if (property != null)
            {
                object propertyValue = property.GetValue(behaviour);
                if (propertyValue != null)
                {
                    value = propertyValue.GetHashCode();
                    return true;
                }
            }

            System.Reflection.FieldInfo field = type.GetField(memberName);
            if (field != null)
            {
                object fieldValue = field.GetValue(behaviour);
                if (fieldValue != null)
                {
                    value = fieldValue.GetHashCode();
                    return true;
                }
            }

            value = NoPlayerId;
            return false;
        }
    }
}
