using ExtractionShooter.Core.Raid;
using ExtractionShooter.Core.Visuals;
using ExtractionShooter.Gameplay.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace ExtractionShooter.Gameplay.Raid
{
    /// <summary>
    /// Signal smoke over an extraction point, seen from across the map: a column of puffs (<see cref="SmokeColumn"/>)
    /// rising from a flare at this transform and drifting with the wind. Green when the point is open, amber for a paid
    /// exit, none while a late-raid point is still shut (it fades in when the point opens). Purely visual and local:
    /// it does not block sight (bots see through it) and nothing is sent over the network. Off on headless servers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ExtractionSmoke : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private ExtractionZone _zone;
        [SerializeField] private Mesh _puffMesh;

        [Tooltip("A transparent unlit material; each puff is tinted through a property block.")]
        [SerializeField] private Material _puffMaterial;

        [SerializeField, Range(4, 64)] private int _puffCount = 26;
        [SerializeField, Min(0.5f)] private float _lifetime = 7f;
        [SerializeField, Min(0f)] private float _riseHeight = 16f;
        [SerializeField, Min(0f)] private float _drift = 6f;
        [SerializeField, Min(0f)] private float _sway = 0.9f;
        [SerializeField, Min(0f)] private float _startRadius = 0.3f;
        [SerializeField, Min(0f)] private float _endRadius = 2.8f;

        [Tooltip("Wind direction (world, horizontal); its length does not matter.")]
        [SerializeField] private Vector3 _wind = new Vector3(1f, 0f, 0.35f);

        [SerializeField] private Color _openColor = new Color(0.32f, 0.92f, 0.42f, 0.55f);
        [SerializeField] private Color _paidColor = new Color(1f, 0.74f, 0.28f, 0.55f);

        [Tooltip("Seconds to fade the whole column in or out when the point opens or closes.")]
        [SerializeField, Min(0.1f)] private float _fadeSeconds = 2.5f;

        private Transform[] _puffs;
        private MeshRenderer[] _renderers;
        private MaterialPropertyBlock _block;
        private float _intensity;
        private bool _visible = true;
        private Color _color;

        private void Awake()
        {
            if (Application.isBatchMode || _puffMesh == null || _puffMaterial == null)
            {
                if (!Application.isBatchMode)
                    Debug.LogError($"[{nameof(ExtractionSmoke)}] {name}: puff mesh or material is not assigned.", this);
                enabled = false;
                return;
            }
            if (_zone == null) _zone = GetComponentInParent<ExtractionZone>();

            _block = new MaterialPropertyBlock();
            _puffs = new Transform[_puffCount];
            _renderers = new MeshRenderer[_puffCount];
            for (int i = 0; i < _puffCount; i++)
            {
                var puff = new GameObject($"Puff {i}");
                puff.transform.SetParent(transform, false);
                puff.AddComponent<MeshFilter>().sharedMesh = _puffMesh;
                MeshRenderer meshRenderer = puff.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = _puffMaterial;
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
                meshRenderer.lightProbeUsage = LightProbeUsage.Off;
                meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _puffs[i] = puff.transform;
                _renderers[i] = meshRenderer;
            }
            _color = _openColor;
            SetVisible(false);
        }

        private void Update()
        {
            ExtractionSmokeTone tone = _zone != null ? ExtractionSignals.SmokeTone(_zone.Rule, LocalSecondsLeft()) : ExtractionSmokeTone.Open;
            if (tone != ExtractionSmokeTone.Off) _color = tone == ExtractionSmokeTone.Paid ? _paidColor : _openColor;

            float target = tone == ExtractionSmokeTone.Off ? 0f : 1f;
            _intensity = Mathf.MoveTowards(_intensity, target, Time.deltaTime / _fadeSeconds);
            if (_intensity <= 0f)
            {
                SetVisible(false);
                return;
            }
            SetVisible(true);

            var shape = new SmokeColumnShape(_lifetime, _riseHeight, _drift, _sway, _startRadius, _endRadius);
            Vector3 along = Vector3.ProjectOnPlane(_wind, Vector3.up);
            along = along.sqrMagnitude > 1e-4f ? along.normalized : Vector3.right;
            Vector3 across = Vector3.Cross(Vector3.up, along);
            Vector3 origin = transform.position;
            double time = Time.timeAsDouble;

            for (int i = 0; i < _puffs.Length; i++)
            {
                SmokePuff puff = SmokeColumn.Evaluate(shape, i, _puffs.Length, time);
                _puffs[i].position = origin + Vector3.up * puff.Height + along * puff.Along + across * puff.Across;
                _puffs[i].localScale = Vector3.one * (puff.Radius * 2f);

                Color color = _color;
                color.a *= puff.Opacity * _intensity;
                _block.SetColor(BaseColorId, color);
                _renderers[i].SetPropertyBlock(_block);
            }
        }

        /// <summary>The local player's raid clock, or null when unknown (a late-raid point then shows no smoke).</summary>
        private static int? LocalSecondsLeft()
        {
            PlayerCharacter local = PlayerCharacter.Local;
            PlayerRaid raid = local != null ? local.Raid : null;
            // 0 until the server has sent the clock: unknown, so a late-raid point does not flash open on spawn.
            return raid != null && raid.SecondsLeft > 0 ? raid.SecondsLeft : (int?)null;
        }

        private void SetVisible(bool visible)
        {
            if (_visible == visible || _renderers == null) return;
            _visible = visible;
            foreach (MeshRenderer meshRenderer in _renderers)
                if (meshRenderer != null) meshRenderer.enabled = visible;
        }
    }
}
