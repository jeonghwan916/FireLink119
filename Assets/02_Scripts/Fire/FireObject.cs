using System;
using Fusion;
using UnityEngine;

namespace FireLink119.Fire
{
    [RequireComponent(typeof(NetworkObject))]
    public class FireObject : NetworkBehaviour, IStateAuthorityChanged
    {
        private struct ParticleInitialState
        {
            public ParticleSystem Particle;
            public ParticleSystem.MinMaxCurve RateOverTime;
            public ParticleSystem.MinMaxCurve StartSize;
            public ParticleSystem.MinMaxCurve StartLifetime;
        }

        [Header("Particle System")]
        [SerializeField] private ParticleSystem[] _fireParticles;

        [Header("Extinguish")]
        [SerializeField] private float _extinguishDuration = 4f;
        [SerializeField] private int _extinguishStageCount = 4;
        [SerializeField] private bool _disableCollidersWhenExtinguished = true;
        [SerializeField] private Collider[] _fireColliders;

        [Header("Authority")]
        [SerializeField] private float _masterAuthorityRequestInterval = 1f;

        [Header("Debug")]
        [SerializeField] private bool _logDebug;

        [Networked] private float ExtinguishProgress { get; set; }
        [Networked] private int CurrentStage { get; set; }
        [Networked] private NetworkBool IsExtinguished { get; set; }

        public bool NetworkIsExtinguished => Object != null && IsExtinguished;
        public event Action OnExtinguished;

        private ParticleInitialState[] _particleInitialStates;
        private AudioSource _audioSource;
        private float _initialVolume;
        private int _lastRenderedStage = -1;
        private bool _lastRenderedExtinguished;
        private bool _hasRaisedExtinguishedEvent;
        private float _lastMasterAuthorityRequestTime = float.NegativeInfinity;

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            _initialVolume = _audioSource != null ? _audioSource.volume : 0f;

            if (_fireParticles == null || _fireParticles.Length == 0)
            {
                _fireParticles = GetComponentsInChildren<ParticleSystem>();
            }

            if (_fireColliders == null)
            {
                _fireColliders = Array.Empty<Collider>();
            }

            _particleInitialStates = new ParticleInitialState[_fireParticles.Length];

            for (int i = 0; i < _fireParticles.Length; i++)
            {
                ParticleSystem particle = _fireParticles[i];
                if (particle == null)
                {
                    continue;
                }

                ParticleSystem.EmissionModule emission = particle.emission;
                ParticleSystem.MainModule main = particle.main;

                _particleInitialStates[i] = new ParticleInitialState
                {
                    Particle = particle,
                    RateOverTime = emission.rateOverTime,
                    StartSize = main.startSize,
                    StartLifetime = main.startLifetime
                };
            }
        }

        public override void Spawned()
        {
            _hasRaisedExtinguishedEvent = false;

            if (HasStateAuthority)
            {
                ExtinguishProgress = 0f;
                CurrentStage = 0;
                IsExtinguished = false;
            }

            ApplyNetworkState(force: true);
            LogDebug($"Spawned. local={Runner.LocalPlayer}, stateAuthority={Object.StateAuthority}, hasStateAuthority={HasStateAuthority}, isMasterClient={Runner.IsSharedModeMasterClient}");
        }

        public void StateAuthorityChanged()
        {
            LogDebug($"StateAuthorityChanged. local={Runner.LocalPlayer}, stateAuthority={Object.StateAuthority}, hasStateAuthority={HasStateAuthority}, isMasterClient={Runner.IsSharedModeMasterClient}");
        }

        public override void FixedUpdateNetwork()
        {
            if (Object == null || Runner == null)
            {
                return;
            }

            EnsureMasterClientAuthority();
        }

        public override void Render()
        {
            ApplyNetworkState(force: false);
        }

        public void TakeExtinguish(float deltaTime)
        {
            if (!CanApplyExtinguish(deltaTime))
            {
                return;
            }

            float previousProgress = ExtinguishProgress;
            int previousStage = CurrentStage;
            float duration = Mathf.Max(_extinguishDuration, 0.01f);
            int stageCount = Mathf.Max(_extinguishStageCount, 1);

            ExtinguishProgress = Mathf.Min(ExtinguishProgress + deltaTime, duration);
            CurrentStage = Mathf.Clamp(
                Mathf.FloorToInt((ExtinguishProgress / duration) * stageCount),
                0,
                stageCount);

            if (ExtinguishProgress >= duration)
            {
                CurrentStage = stageCount;
                IsExtinguished = true;
            }

            LogDebug($"TakeExtinguish applied. local={Runner.LocalPlayer}, deltaTime={deltaTime:0.000}, progress={previousProgress:0.000}->{ExtinguishProgress:0.000}, stage={previousStage}->{CurrentStage}, isExtinguished={IsExtinguished}");
        }

        private void EnsureMasterClientAuthority()
        {
            if (Runner.IsSharedModeMasterClient)
            {
                RequestAuthorityIfNeeded();
                return;
            }

            ReleaseAuthorityIfHeldByNonMaster();
        }

        private void RequestAuthorityIfNeeded()
        {
            if (HasStateAuthority ||
                Time.time - _lastMasterAuthorityRequestTime < _masterAuthorityRequestInterval)
            {
                return;
            }

            _lastMasterAuthorityRequestTime = Time.time;
            Runner.RequestStateAuthority(Object.Id);
            LogDebug($"MasterClient requested StateAuthority. local={Runner.LocalPlayer}, currentStateAuthority={Object.StateAuthority}");
        }

        private void ReleaseAuthorityIfHeldByNonMaster()
        {
            if (!HasStateAuthority)
            {
                return;
            }

            Runner.ReleaseStateAuthority(Object.Id);
            LogDebug($"Non-master released StateAuthority. local={Runner.LocalPlayer}");
        }

        private bool CanApplyExtinguish(float deltaTime)
        {
            if (Runner == null || Object == null)
            {
                return false;
            }

            if (!Runner.IsSharedModeMasterClient || !HasStateAuthority)
            {
                LogDebug($"TakeExtinguish blocked by authority. local={Runner.LocalPlayer}, isMasterClient={Runner.IsSharedModeMasterClient}, hasStateAuthority={HasStateAuthority}, stateAuthority={Object.StateAuthority}, deltaTime={deltaTime:0.000}");
                return false;
            }

            if (IsExtinguished || deltaTime <= 0f)
            {
                return false;
            }

            return true;
        }

        private void ApplyNetworkState(bool force)
        {
            if (!force &&
                _lastRenderedStage == CurrentStage &&
                _lastRenderedExtinguished == IsExtinguished)
            {
                return;
            }

            _lastRenderedStage = CurrentStage;
            _lastRenderedExtinguished = IsExtinguished;

            int stageCount = Mathf.Max(_extinguishStageCount, 1);
            float intensity = IsExtinguished ? 0f : 1f - ((float)CurrentStage / stageCount);
            ApplyIntensity(Mathf.Clamp01(intensity));
            ApplyColliderState();

            if (IsExtinguished)
            {
                ApplyExtinguishedVisuals();
                RaiseExtinguishedEventOnce();
            }
        }

        private void ApplyIntensity(float intensity)
        {
            foreach (ParticleInitialState state in _particleInitialStates)
            {
                if (state.Particle == null)
                {
                    continue;
                }

                ParticleSystem.EmissionModule emission = state.Particle.emission;
                emission.rateOverTime = ScaleCurve(state.RateOverTime, intensity);

                ParticleSystem.MainModule main = state.Particle.main;
                main.startSize = ScaleCurve(state.StartSize, intensity);
                main.startLifetime = ScaleCurve(state.StartLifetime, intensity);
            }

            if (_audioSource != null)
            {
                _audioSource.volume = _initialVolume * intensity;
            }
        }

        private void ApplyExtinguishedVisuals()
        {
            foreach (ParticleSystem particle in _fireParticles)
            {
                if (particle != null)
                {
                    particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            if (_audioSource != null)
            {
                _audioSource.volume = 0f;
            }
        }

        private void ApplyColliderState()
        {
            if (!_disableCollidersWhenExtinguished)
            {
                return;
            }

            foreach (Collider fireCollider in _fireColliders)
            {
                if (fireCollider != null)
                {
                    fireCollider.enabled = !IsExtinguished;
                }
            }
        }

        private void RaiseExtinguishedEventOnce()
        {
            if (_hasRaisedExtinguishedEvent)
            {
                return;
            }

            _hasRaisedExtinguishedEvent = true;
            OnExtinguished?.Invoke();
        }

        private ParticleSystem.MinMaxCurve ScaleCurve(ParticleSystem.MinMaxCurve curve, float scale)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    curve.constant *= scale;
                    break;
                case ParticleSystemCurveMode.TwoConstants:
                    curve.constantMin *= scale;
                    curve.constantMax *= scale;
                    break;
                case ParticleSystemCurveMode.Curve:
                case ParticleSystemCurveMode.TwoCurves:
                    curve.curveMultiplier *= scale;
                    break;
            }

            return curve;
        }

        private void LogDebug(string message)
        {
            if (_logDebug)
            {
                Debug.Log($"[FireObject] {message}", this);
            }
        }
    }
}
