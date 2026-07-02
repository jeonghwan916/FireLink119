using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FireLink119.NPC
{
    [RequireComponent(typeof(XRBaseInteractable))]
    public class NPCShoulderGrabTrigger : MonoBehaviour
    {
        [SerializeField] private NPCController _npcController;
        [SerializeField, Range(0f, 1f)] private float _hapticAmplitude = 0.5f;
        [SerializeField] private float _hapticDuration = 0.08f;

        private XRBaseInteractable _interactable;

        private void Awake()
        {
            if (_npcController == null)
            {
                _npcController = GetComponentInParent<NPCController>();
            }

            _interactable = GetComponent<XRBaseInteractable>();
        }

        private void OnEnable()
        {
            _interactable.selectEntered.AddListener(OnSelectEntered);
        }

        private void OnDisable()
        {
            _interactable.selectEntered.RemoveListener(OnSelectEntered);
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (args.interactorObject is XRBaseInputInteractor inputInteractor)
            {
                inputInteractor.SendHapticImpulse(_hapticAmplitude, _hapticDuration);
            }

            if (_npcController == null || _npcController.Runner == null)
            {
                return;
            }

            PlayerRef localPlayer = _npcController.Runner.LocalPlayer;
            _npcController.RequestFollowPlayer(localPlayer);
        }
    }
}