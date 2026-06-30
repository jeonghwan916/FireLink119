using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;


namespace FireLink119.Extinguisher
{
    public class Extinguisher : MonoBehaviour
    {
        [SerializeField] private ParticleSystem smokeParticle;
        private XRGrabInteractable grabInteractable;

        void Awake()
        {
            grabInteractable = GetComponent<XRGrabInteractable>();
        }

        void OnEnable()
        {
            // 이벤트 구독
            grabInteractable.activated.AddListener(OnFireStart);
            grabInteractable.deactivated.AddListener(OnFireEnd);
        }

        void OnDisable()
        {
            // 이벤트 해제
            grabInteractable.activated.RemoveListener(OnFireStart);
            grabInteractable.deactivated.RemoveListener(OnFireEnd);
        }

        private void OnFireStart(ActivateEventArgs args)
        {
            if (smokeParticle != null) smokeParticle.Play();
            // TODO: 4번 불 진화 로직 시작 (예: 레이캐스트 또는 트리거 콜라이더 활성화)
        }

        private void OnFireEnd(DeactivateEventArgs args)
        {
            if (smokeParticle != null) smokeParticle.Stop();
            // TODO: 4번 불 진화 로직 정지
        }
    }
}
