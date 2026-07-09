using FireLink119.Player;
using Fusion;
using UnityEngine;

namespace FireLink119.Network
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Collider))]
    public class NetworkNarrationTrigger : NetworkBehaviour
    {
        [SerializeField] private AudioClip _narrationClip;
        [SerializeField] private float _volume = 1f;
        [SerializeField] private string _playerTag = "Player";
        [SerializeField] private bool _allowPlayerTagContact = true;

        [Networked] private NetworkBool HasEntered { get; set; }
        [Networked] private int NarrationEventId { get; set; }

        private int _lastHandledNarrationEventId;

        public override void Spawned()
        {
            _lastHandledNarrationEventId = NarrationEventId;
        }

        public override void Render()
        {
            if (NarrationEventId == _lastHandledNarrationEventId)
            {
                return;
            }

            _lastHandledNarrationEventId = NarrationEventId;

            if (_narrationClip != null)
            {
                LocalNarrationAudio.PlayOneShot(_narrationClip, _volume);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!HasStateAuthority || HasEntered || !IsPlayerContact(other))
            {
                return;
            }

            HasEntered = true;
            NarrationEventId++;
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
    }
}
