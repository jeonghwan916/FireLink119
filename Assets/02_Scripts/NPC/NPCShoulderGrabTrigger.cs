using System.Collections.Generic;
using Fusion;
using FireLink119.Player;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FireLink119.NPC
{
    [RequireComponent(typeof(XRBaseInteractable))]
    [RequireComponent(typeof(Collider))]
    public class NPCShoulderGrabTrigger : MonoBehaviour
    {
        private static readonly Dictionary<int, float> NextActivationTimeByNpc = new Dictionary<int, float>();

        [SerializeField] private NPCController _npcController;
        [SerializeField] private string _playerTag = "Player";
        [SerializeField] private bool _allowPlayerTagContact = true;
        [SerializeField] private float _activationCooldown = 1f;
        [SerializeField, Range(0f, 1f)] private float _hapticAmplitude = 0.5f;
        [SerializeField] private float _hapticDuration = 0.08f;

        private XRBaseInteractable _interactable;
        private float _nextActivationTime;

        private void Awake()
        {
            _interactable = GetComponent<XRBaseInteractable>();

            if (_npcController == null)
            {
                _npcController = GetComponentInParent<NPCController>();
            }
        }

        private void OnEnable()
        {
            _interactable.hoverEntered.AddListener(OnHoverEntered);
        }

        private void OnDisable()
        {
            _interactable.hoverEntered.RemoveListener(OnHoverEntered);
        }

        private void OnDestroy()
        {
            if (_npcController != null)
            {
                NextActivationTimeByNpc.Remove(_npcController.GetInstanceID());
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            TryActivateFromCollider(other);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision == null)
            {
                return;
            }

            TryActivateFromCollider(collision.collider);
        }

        private void OnHoverEntered(HoverEnterEventArgs args)
        {
            if (args.interactorObject == null || IsRayInteractor(args.interactorObject))
            {
                return;
            }

            XRBaseInputInteractor inputInteractor = args.interactorObject as XRBaseInputInteractor;
            TryActivate(inputInteractor);
        }

        private void TryActivateFromCollider(Collider other)
        {
            if (other == null || IsOwnNpcCollider(other))
            {
                return;
            }

            XRBaseInputInteractor inputInteractor = other.GetComponentInParent<XRBaseInputInteractor>();
            if (inputInteractor != null)
            {
                if (!IsRayInteractor(inputInteractor))
                {
                    TryActivate(inputInteractor);
                }

                return;
            }

            if (!IsPlayerContact(other))
            {
                return;
            }

            TryActivate(inputInteractor: null);
        }

        private void TryActivate(XRBaseInputInteractor inputInteractor)
        {
            if (Time.time < _nextActivationTime)
            {
                return;
            }

            if (_npcController == null || _npcController.Runner == null)
            {
                return;
            }

            int cooldownKey = _npcController.GetInstanceID();
            if (NextActivationTimeByNpc.TryGetValue(cooldownKey, out float nextNpcActivationTime) &&
                Time.time < nextNpcActivationTime)
            {
                return;
            }

            float nextActivationTime = Time.time + Mathf.Max(0f, _activationCooldown);
            _nextActivationTime = nextActivationTime;
            NextActivationTimeByNpc[cooldownKey] = nextActivationTime;

            SendHaptic(inputInteractor);

            PlayerRef localPlayer = _npcController.Runner.LocalPlayer;
            _npcController.RequestFollowPlayer(localPlayer);
        }

        private bool IsOwnNpcCollider(Collider other)
        {
            return _npcController != null && other.GetComponentInParent<NPCController>() == _npcController;
        }

        private bool IsPlayerContact(Collider other)
        {
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

        private static bool IsRayInteractor(object interactor)
        {
            return interactor != null && interactor.GetType().Name.Contains("Ray");
        }

        private void SendHaptic(XRBaseInputInteractor inputInteractor)
        {
            if (inputInteractor != null)
            {
                inputInteractor.SendHapticImpulse(_hapticAmplitude, _hapticDuration);
            }
        }
    }
}
