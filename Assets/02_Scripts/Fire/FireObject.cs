using System;
using UnityEngine;

namespace FireLink119.Fire
{
    public class FireObject : MonoBehaviour
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
        [SerializeField] private bool _disableWhenExtinguished = true;

        private ParticleInitialState[] _particleInitialStates;
        private float _accumulatedExtinguishTime;
        private int _currentStage;
        private bool _isExtinguished;
        
        // 불이 꺼졌을때의 후속 동작이 필요하다면 여기 이벤트 구독하면 됨
        public event Action OnExtinguished;

        private void Awake()
        {
            if (_fireParticles == null || _fireParticles.Length == 0)
                _fireParticles = GetComponentsInChildren<ParticleSystem>();

            _particleInitialStates = new ParticleInitialState[_fireParticles.Length];

            for (int i = 0; i < _fireParticles.Length; i++)
            {
                ParticleSystem ps = _fireParticles[i];
                if (ps == null) continue;

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

        public void TakeExtinguish(float deltaTime)
        {
            if (_isExtinguished || deltaTime <= 0f) return;

            _accumulatedExtinguishTime += deltaTime;

            float duration = Mathf.Max(_extinguishDuration, 0.01f);
            int stageCount = Mathf.Max(_extinguishStageCount, 1);
            float secondsPerStage = duration / stageCount;

            int nextStage = Mathf.FloorToInt(_accumulatedExtinguishTime / secondsPerStage);
            nextStage = Mathf.Clamp(nextStage, 0, stageCount);

            if (nextStage == _currentStage) return;

            _currentStage = nextStage;

            float intensity = 1f - ((float)_currentStage / stageCount);
            ApplyIntensity(intensity);

            if (_currentStage >= stageCount)
            {
                Extinguish();
            }
        }

        private void ApplyIntensity(float intensity)
        {
            foreach (ParticleInitialState state in _particleInitialStates)
            {
                if (state.Particle == null) continue;

                var emission = state.Particle.emission;
                emission.rateOverTime = ScaleCurve(state.RateOverTime, intensity);

                var main = state.Particle.main;
                main.startSize = ScaleCurve(state.StartSize, intensity);
                main.startLifetime = ScaleCurve(state.StartLifetime, intensity);
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

        private void Extinguish()
        {
            _isExtinguished = true;

            // 여기에서 불꺼졌을때의 이벤트 발행
            OnExtinguished?.Invoke();
            
            foreach (ParticleSystem ps in _fireParticles)
            {
                if (ps == null) continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            if (_disableWhenExtinguished)
            {
                gameObject.SetActive(false);
            }
        }
    }
}