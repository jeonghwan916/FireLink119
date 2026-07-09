using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FireLink119.Ending
{
    public class EndingDanceShowcase : MonoBehaviour
    {
        [Serializable]
        private struct DancerSetup
        {
            [SerializeField] private GameObject _prefab;
            [SerializeField] private AnimationClip _animationClip;
            [SerializeField] private string _prefabAssetPath;
            [SerializeField] private string _animationAssetPath;
            [SerializeField] private string _animationClipName;
            [SerializeField] private string _name;
            [SerializeField] private Vector3 _localPosition;
            [SerializeField] private Vector3 _localEulerAngles;
            [SerializeField] private Vector3 _localScale;
            [SerializeField] private float _speed;

            public GameObject Prefab => ResolvePrefab();
            public AnimationClip AnimationClip => ResolveAnimationClip();
            public string Name => _name;
            public Vector3 LocalPosition => _localPosition;
            public Vector3 LocalEulerAngles => _localEulerAngles;
            public Vector3 LocalScale => _localScale == Vector3.zero ? Vector3.one : _localScale;
            public float Speed => _speed <= 0f ? 1f : _speed;

            private GameObject ResolvePrefab()
            {
                if (_prefab != null)
                {
                    return _prefab;
                }

#if UNITY_EDITOR
                if (!string.IsNullOrWhiteSpace(_prefabAssetPath))
                {
                    return AssetDatabase.LoadAssetAtPath<GameObject>(_prefabAssetPath);
                }
#endif

                return null;
            }

            private AnimationClip ResolveAnimationClip()
            {
                if (_animationClip != null)
                {
                    return _animationClip;
                }

#if UNITY_EDITOR
                if (string.IsNullOrWhiteSpace(_animationAssetPath))
                {
                    return null;
                }

                UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(_animationAssetPath);
                AnimationClip fallbackClip = null;
                for (int i = 0; i < assets.Length; i++)
                {
                    if (assets[i] is not AnimationClip clip)
                    {
                        continue;
                    }

                    if (!clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase) &&
                        fallbackClip == null)
                    {
                        fallbackClip = clip;
                    }

                    if (string.IsNullOrWhiteSpace(_animationClipName) || clip.name == _animationClipName)
                    {
                        return clip;
                    }
                }

                return fallbackClip;
#else
                return null;
#endif
            }
        }

        private sealed class DancerRuntime
        {
            public PlayableGraph Graph;
            public AnimationClipPlayable Playable;
            public AnimationClip Clip;
            public float Speed;
            public float Time;
        }

        [SerializeField] private DancerSetup[] _dancers;
        [SerializeField] private bool _playOnStart = true;

        private readonly List<DancerRuntime> _runtimes = new List<DancerRuntime>();

        private void Start()
        {
            if (_playOnStart)
            {
                SpawnDancers();
            }
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            for (int i = 0; i < _runtimes.Count; i++)
            {
                DancerRuntime runtime = _runtimes[i];
                if (!runtime.Graph.IsValid() || runtime.Clip == null || runtime.Clip.length <= 0f)
                {
                    continue;
                }

                runtime.Time = Mathf.Repeat(runtime.Time + deltaTime * runtime.Speed, runtime.Clip.length);
                runtime.Playable.SetTime(runtime.Time);
                runtime.Graph.Evaluate(0f);
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _runtimes.Count; i++)
            {
                PlayableGraph graph = _runtimes[i].Graph;
                if (graph.IsValid())
                {
                    graph.Destroy();
                }
            }

            _runtimes.Clear();
        }

        private void SpawnDancers()
        {
            for (int i = 0; i < _dancers.Length; i++)
            {
                SpawnDancer(_dancers[i], i);
            }
        }

        private void SpawnDancer(DancerSetup setup, int index)
        {
            GameObject prefab = setup.Prefab;
            AnimationClip animationClip = setup.AnimationClip;

            if (prefab == null || animationClip == null)
            {
                Debug.LogWarning($"[EndingDanceShowcase] Dancer {index} is missing a prefab or animation clip.", this);
                return;
            }

            GameObject instance = Instantiate(prefab, transform);
            instance.name = string.IsNullOrWhiteSpace(setup.Name) ? prefab.name : setup.Name;
            instance.transform.localPosition = setup.LocalPosition;
            instance.transform.localRotation = Quaternion.Euler(setup.LocalEulerAngles);
            instance.transform.localScale = setup.LocalScale;

            Animator animator = ResolveAnimator(instance);
            if (animator == null)
            {
                Debug.LogWarning($"[EndingDanceShowcase] Animator was not found on {instance.name}.", this);
                return;
            }

            // Ending 연출은 네트워크/이동 로직과 분리되어야 하므로 루트 모션으로 위치가 밀리지 않게 고정한다.
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            PlayableGraph graph = PlayableGraph.Create($"{instance.name} Dance");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

            AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, animationClip);
            playable.SetSpeed(0f);
            playable.SetApplyFootIK(true);

            AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Animation", animator);
            output.SetSourcePlayable(playable);

            graph.Play();
            playable.SetTime(0f);
            graph.Evaluate(0f);

            _runtimes.Add(new DancerRuntime
            {
                Graph = graph,
                Playable = playable,
                Clip = animationClip,
                Speed = setup.Speed,
                Time = 0f
            });
        }

        private static Animator ResolveAnimator(GameObject instance)
        {
            Animator animator = instance.GetComponent<Animator>();
            if (animator != null)
            {
                return animator;
            }

            return instance.GetComponentInChildren<Animator>();
        }
    }
}
