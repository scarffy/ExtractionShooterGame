using UnityEngine;

namespace ExtractionShooter.Gameplay.Weapons
{
    /// <summary>
    /// Client-side tracer effect for any <see cref="IShotSource"/> on the same object (player weapon, bot). Uses a small
    /// pool of LineRenderers created on the first shot, so a dedicated server
    /// never allocates any.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitscanWeaponView : MonoBehaviour
    {
        [Tooltip("Unlit material for tracers.")]
        [SerializeField] private Material _tracerMaterial;

        [SerializeField] private Color _tracerColor = new Color(1f, 0.85f, 0.3f);
        [SerializeField, Min(0.001f)] private float _tracerWidth = 0.03f;
        [SerializeField, Min(0.01f)] private float _tracerLifetime = 0.06f;
        [Tooltip("Enough for a full shotgun shell plus a few rounds in flight.")]
        [SerializeField, Min(1)] private int _poolSize = 16;

        private IShotSource _weapon;
        private LineRenderer[] _pool;
        private float[] _expiresAt;
        private int _nextIndex;

        private void Awake()
        {
            _weapon = GetComponent<IShotSource>();
            if (_weapon == null)
            {
                Debug.LogError($"[{nameof(HitscanWeaponView)}] No {nameof(IShotSource)} on {name}.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (_weapon != null) _weapon.ShotConfirmed += PlayTracer;
        }

        private void OnDisable()
        {
            if (_weapon != null) _weapon.ShotConfirmed -= PlayTracer;
        }

        private void Update()
        {
            if (_pool == null) return;

            float now = Time.time;
            for (int i = 0; i < _pool.Length; i++)
            {
                if (_pool[i].enabled && now >= _expiresAt[i])
                    _pool[i].enabled = false;
            }
        }

        private void PlayTracer(Vector3 origin, Vector3 end)
        {
            EnsurePool();

            LineRenderer line = _pool[_nextIndex];
            line.SetPosition(0, origin);
            line.SetPosition(1, end);
            line.enabled = true;
            _expiresAt[_nextIndex] = Time.time + _tracerLifetime;

            _nextIndex = (_nextIndex + 1) % _pool.Length;
        }

        private void EnsurePool()
        {
            if (_pool != null) return;

            if (_tracerMaterial == null)
                Debug.LogWarning($"[{nameof(HitscanWeaponView)}] Tracer material is not assigned on {name}.", this);

            var root = new GameObject("Tracers").transform;
            root.SetParent(transform, false);

            _pool = new LineRenderer[_poolSize];
            _expiresAt = new float[_poolSize];
            for (int i = 0; i < _poolSize; i++)
            {
                var line = new GameObject($"Tracer_{i}").AddComponent<LineRenderer>();
                line.transform.SetParent(root, false);
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.startWidth = line.endWidth = _tracerWidth;
                line.startColor = line.endColor = _tracerColor;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.sharedMaterial = _tracerMaterial;
                line.enabled = false;
                _pool[i] = line;
            }
        }
    }
}
