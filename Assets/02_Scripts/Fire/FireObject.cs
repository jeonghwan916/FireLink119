using System;
using Fusion;
using UnityEngine;

namespace FireLink119.Fire
{
    public class FireObject : NetworkBehaviour
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

        [Header("Extinguish : Stages")]
        [SerializeField] private float _extinguishDuration = 4f;
        [SerializeField] private int _extinguishStageCount = 4;
        [SerializeField] private bool _disableCollidersWhenExtinguished = true;
        [SerializeField] private Collider[] _fireColliders;

        [Networked] private float ExtinguishProgress { get; set; }
        [Networked] private int CurrentStage { get; set; }
        [Networked] private NetworkBool IsExtinguished { get; set; }

        public event Action OnExtinguished;

        private ParticleInitialState[] _particleInitialStates;
        private AudioSource _audioSource;
        private float _initialVolume;
        private int _lastRenderedStage = -1;
        private bool _lastRenderedExtinguished;

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource != null)
            {
                _initialVolume = _audioSource.volume;
            }

            if (_fireParticles == null || _fireParticles.Length == 0)
            {
                _fireParticles = GetComponentsInChildren<ParticleSystem>();
            }

            if (_fireColliders == null || _fireColliders.Length == 0)
            {
                _fireColliders = GetComponentsInChildren<Collider>();
            }

            _particleInitialStates = new ParticleInitialState[_fireParticles.Length];

            for (int i = 0; i < _fireParticles.Length; i++)
            {
                ParticleSystem ps = _fireParticles[i];
                if (ps == null)
                {
                    continue;
                }

                var emission = ps.emission;
                var main = ps.main;

                _particleInitialStates[i] = new ParticleInitialState
                {
                    Particle = ps,
                    RateOverTime = emission.rateOverTime,
                    StartSize = main.startSize,
                    StartLifetime = main.startLifetime
                };
            }
        }

        public override void Spawned()
        {
            if (HasStateAuthority)
            {
                ExtinguishProgress = 0f;
                CurrentStage = 0;
                IsExtinguished = false;
            }

            ApplyNetworkState(force: true);
        }

        public override void Render()
        {
            ApplyNetworkState(force: false);
        }

        public void TakeExtinguish(float deltaTime)
        {
            if (!HasStateAuthority || IsExtinguished || deltaTime <= 0f)
            {
                return;
            }

            ExtinguishProgress += deltaTime;

            float duration = Mathf.Max(_extinguishDuration, 0.01f);
            int stageCount = Mathf.Max(_extinguishStageCount, 1);
            float secondsPerStage = duration / stageCount;

            int nextStage = Mathf.FloorToInt(ExtinguishProgress / secondsPerStage);
            nextStage = Mathf.Clamp(nextStage, 0, stageCount);

            if (nextStage != CurrentStage)
            {
                CurrentStage = nextStage;
            }

            if (CurrentStage >= stageCount)
            {
                IsExtinguished = true;
            }
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
            ApplyIntensity(intensity);

            if (IsExtinguished)
            {
                ApplyExtinguishedVisuals();
                OnExtinguished?.Invoke();
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

                var emission = state.Particle.emission;
                emission.rateOverTime = ScaleCurve(state.RateOverTime, intensity);

                var main = state.Particle.main;
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
            foreach (ParticleSystem ps in _fireParticles)
            {
                if (ps == null)
                {
                    continue;
                }

                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            if (_audioSource != null)
            {
                _audioSource.volume = 0f;
            }

            if (_disableCollidersWhenExtinguished)
            {
                foreach (Collider fireCollider in _fireColliders)
                {
                    if (fireCollider != null)
                    {
                        fireCollider.enabled = false;
                    }
                }
            }
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
    }
}