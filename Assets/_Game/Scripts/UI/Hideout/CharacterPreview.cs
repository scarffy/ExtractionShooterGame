using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Visuals;
using UnityEngine;

namespace ExtractionShooter.UI.Hideout
{
    /// <summary>
    /// The character preview in the stash and inventory screens: the same <see cref="CharacterBody"/> players see in a
    /// raid, dressed in the shown loadout (helmet, armor, rig, backpack, knife, the first primary in hand, other weapons
    /// slung and holstered), standing at the ready and turning slowly, lit by a key, fill and rim light and rendered by its
    /// own camera into a texture the screens show. The stage lives far below the map on its own layer, so the game camera
    /// never sees it and its lights touch nothing else.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterPreview : MonoBehaviour
    {
        [SerializeField] private VisualProfile _profile;

        [Tooltip("Where the figure stands: far from anything the game camera can see.")]
        [SerializeField] private Vector3 _stagePosition = new Vector3(0f, -500f, 0f);

        [Tooltip("Layer only the preview camera renders.")]
        [SerializeField, Range(0, 31)] private int _layer = 31;

        [SerializeField, Min(64)] private int _textureWidth = 512;
        [SerializeField, Min(64)] private int _textureHeight = 1024;

        [Tooltip("Turn speed in degrees per second.")]
        [SerializeField] private float _turnDegreesPerSecond = 14f;

        [Header("Framing")]
        [SerializeField, Range(10f, 60f)] private float _fieldOfView = 24f;

        [Tooltip("Height (metres) that must fit in the picture: the figure with a helmet and some headroom.")]
        [SerializeField, Min(0.5f)] private float _framingHeight = 2.05f;

        [Tooltip("Width (metres) that must fit in the picture: the figure with a weapon held out in front.")]
        [SerializeField, Min(0.3f)] private float _framingWidth = 1.2f;

        [Tooltip("Height the camera looks at (metres above the feet).")]
        [SerializeField] private float _lookHeight = 0.98f;

        [SerializeField] private Color _background = new Color(0.07f, 0.08f, 0.09f, 1f);

        [Header("Lighting")]
        [SerializeField] private Color _keyColor = new Color(1f, 0.95f, 0.86f);
        [Tooltip("Point lights fall off with distance squared: about 9 lights the figure fully from 3 m.")]
        [SerializeField, Min(0f)] private float _keyIntensity = 9f;
        [SerializeField] private Color _fillColor = new Color(0.62f, 0.72f, 0.85f);
        [SerializeField, Min(0f)] private float _fillIntensity = 3.5f;
        [SerializeField] private Color _rimColor = new Color(0.85f, 0.9f, 1f);
        [SerializeField, Min(0f)] private float _rimIntensity = 7f;

        private readonly Outfit _outfit = new Outfit();

        private RenderTexture _texture;
        private Camera _camera;
        private Transform _stage;
        private Transform _turntable;
        private CharacterBody _body;

        /// <summary>What the preview camera renders. Null while disabled.</summary>
        public RenderTexture Texture => _texture;

        private void OnEnable() => BuildStage();

        private void OnDisable() => DestroyStage();

        private void Update()
        {
            if (_turntable != null) _turntable.Rotate(0f, _turnDegreesPerSecond * Time.deltaTime, 0f);
        }

        /// <summary>Shows the gear in an equipment container (a loadout); null shows the bare figure.</summary>
        public void Show(ItemContainer loadout)
        {
            if (_body == null) return;
            _outfit.ReadFrom(loadout, null);
            _outfit.Drawn = FirstWeapon();
            _body.ApplyOutfit(_outfit);
        }

        /// <summary>The weapon to hold: the first primary, else the second, else the pistol.</summary>
        private EquipmentSlot? FirstWeapon()
        {
            foreach (EquipmentSlot slot in EquipmentRules.WeaponSlots)
                if (_outfit.Get(slot) != null) return slot;
            return null;
        }

        // ---------------- Stage ----------------

        private void BuildStage()
        {
            if (_profile == null)
            {
                Debug.LogError($"[{nameof(CharacterPreview)}] Visual profile is not assigned on {name}.", this);
                return;
            }

            _texture = new RenderTexture(_textureWidth, _textureHeight, 24) { name = "Character Preview" };

            _stage = new GameObject("Character Preview Stage").transform;
            _stage.gameObject.layer = _layer;
            _stage.position = _stagePosition;

            // Turntable (rotates) > body. Built on the preview layer: every part takes its parent's layer.
            _turntable = PrimitiveParts.Node(_stage, "Turntable", Vector3.zero);
            PrimitiveParts.Cylinder(_turntable, "Plinth", new Vector3(0f, -0.01f, 0f), 0.42f, 0.02f, Quaternion.identity,
                _profile.Lit(new Color(0.16f, 0.17f, 0.18f)));
            Transform figure = PrimitiveParts.Node(_turntable, "Figure", Vector3.zero);
            _body = figure.gameObject.AddComponent<CharacterBody>();
            _body.Initialize(_profile, true);
            _body.Build();
            _body.SetPose(new BodyPose());

            // Camera straight in front, far enough for the framing box to fit both ways.
            float aspect = (float)_textureWidth / _textureHeight;
            float halfV = _fieldOfView * 0.5f * Mathf.Deg2Rad;
            float halfH = Mathf.Atan(Mathf.Tan(halfV) * aspect);
            float distance = Mathf.Max(_framingHeight * 0.5f / Mathf.Tan(halfV), _framingWidth * 0.5f / Mathf.Tan(halfH)) + 0.3f;

            _camera = new GameObject("Preview Camera").AddComponent<Camera>();
            _camera.gameObject.layer = _layer;
            _camera.transform.SetParent(_stage, false);
            _camera.transform.localPosition = new Vector3(0f, _lookHeight, distance);
            _camera.transform.localRotation = Quaternion.LookRotation(Vector3.back);
            _camera.fieldOfView = _fieldOfView;
            _camera.aspect = aspect;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = distance + 5f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = _background;
            _camera.cullingMask = 1 << _layer;
            _camera.depth = -10f;
            _camera.targetTexture = _texture;

            // Key (front right, high), fill (front left, low), rim (behind). Point lights: range keeps them on the stage.
            AddLight("Key Light", new Vector3(1.6f, 2.4f, 2.2f), _keyColor, _keyIntensity);
            AddLight("Fill Light", new Vector3(-2f, 1.2f, 1.8f), _fillColor, _fillIntensity);
            AddLight("Rim Light", new Vector3(-0.6f, 2.2f, -2.2f), _rimColor, _rimIntensity);

            Show(null);
        }

        private void AddLight(string lightName, Vector3 position, Color color, float intensity)
        {
            Light light = new GameObject(lightName).AddComponent<Light>();
            light.gameObject.layer = _layer;
            light.transform.SetParent(_stage, false);
            light.transform.localPosition = position;
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = 7f;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << _layer;
        }

        private void DestroyStage()
        {
            if (_camera != null) _camera.targetTexture = null;
            if (_stage != null) Destroy(_stage.gameObject);
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
            _texture = null;
            _camera = null;
            _stage = null;
            _turntable = null;
            _body = null;
        }
    }
}
