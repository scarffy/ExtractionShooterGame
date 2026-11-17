using System.Collections.Generic;
using ExtractionShooter.Gameplay.Inventory;
using UnityEngine;

namespace ExtractionShooter.UI.Hideout
{
    /// <summary>
    /// The lobby's character preview: a simple figure dressed in the loadout (helmet, armor, rig, backpack, guns,
    /// knife), turning slowly, rendered by its own camera into a texture the stash screen shows.
    /// The figure lives far below the map on its own layer, so the game camera never sees it.
    /// Placeholder art: primitives tinted per part until real character models exist.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterPreview : MonoBehaviour
    {
        [SerializeField] private Material _bodyMaterial;
        [SerializeField] private Material _gearMaterial;

        [Tooltip("Where the figure stands: far from anything the game camera can see.")]
        [SerializeField] private Vector3 _stagePosition = new Vector3(0f, -500f, 0f);

        [Tooltip("Layer only the preview camera renders.")]
        [SerializeField, Range(0, 31)] private int _layer = 31;

        [SerializeField, Min(64)] private int _textureWidth = 512;
        [SerializeField, Min(64)] private int _textureHeight = 768;
        [SerializeField] private float _turnDegreesPerSecond = 18f;

        private readonly Dictionary<EquipmentSlot, GameObject> _gear = new Dictionary<EquipmentSlot, GameObject>();
        private readonly List<Material> _materials = new List<Material>();

        private RenderTexture _texture;
        private Camera _camera;
        private Transform _stage;
        private Transform _figure;

        /// <summary>What the preview camera renders. Null while disabled.</summary>
        public RenderTexture Texture => _texture;

        private void OnEnable() => BuildStage();

        private void OnDisable() => DestroyStage();

        private void Update()
        {
            if (_figure != null) _figure.Rotate(0f, _turnDegreesPerSecond * Time.deltaTime, 0f);
        }

        /// <summary>Shows the gear in an equipment container (a loadout); null shows the bare figure.</summary>
        public void Show(ItemContainer loadout)
        {
            foreach (KeyValuePair<EquipmentSlot, GameObject> part in _gear)
            {
                bool worn = loadout != null && !loadout.GetSlot((int)part.Key).IsEmpty;
                if (part.Value.activeSelf != worn) part.Value.SetActive(worn);
            }
        }

        // ---------------- Stage ----------------

        private void BuildStage()
        {
            _texture = new RenderTexture(_textureWidth, _textureHeight, 24) { name = "Character Preview" };

            _stage = new GameObject("Character Preview Stage").transform;
            _stage.position = _stagePosition;

            _figure = new GameObject("Figure").transform;
            _figure.SetParent(_stage, false);

            // Body and visor (always shown).
            Part(PrimitiveType.Capsule, _figure, new Vector3(0f, 1f, 0f), new Vector3(0.8f, 1f, 0.8f), Tint(_bodyMaterial, new Color(0.55f, 0.6f, 0.65f)));
            Part(PrimitiveType.Cube, _figure, new Vector3(0f, 1.62f, 0.33f), new Vector3(0.5f, 0.14f, 0.12f), Tint(_bodyMaterial, new Color(0.1f, 0.12f, 0.14f)));

            Material gear = Tint(_gearMaterial, new Color(0.36f, 0.4f, 0.3f));
            Material dark = Tint(_gearMaterial, new Color(0.18f, 0.19f, 0.2f));
            Material steel = Tint(_gearMaterial, new Color(0.45f, 0.46f, 0.48f));

            _gear[EquipmentSlot.Helmet] = Part(PrimitiveType.Sphere, _figure, new Vector3(0f, 1.82f, 0f), new Vector3(0.62f, 0.42f, 0.66f), gear);
            _gear[EquipmentSlot.BodyArmor] = Part(PrimitiveType.Cube, _figure, new Vector3(0f, 1.18f, 0f), new Vector3(0.86f, 0.62f, 0.88f), gear);
            _gear[EquipmentSlot.ChestRig] = Part(PrimitiveType.Cube, _figure, new Vector3(0f, 1.05f, 0.45f), new Vector3(0.62f, 0.34f, 0.14f), dark);
            _gear[EquipmentSlot.Backpack] = Part(PrimitiveType.Cube, _figure, new Vector3(0f, 1.15f, -0.52f), new Vector3(0.62f, 0.72f, 0.32f), dark);
            _gear[EquipmentSlot.Primary1] = Part(PrimitiveType.Cube, _figure, new Vector3(0.18f, 1.2f, -0.72f), new Vector3(0.1f, 0.12f, 1.1f), steel, new Vector3(-55f, 0f, 30f));
            _gear[EquipmentSlot.Primary2] = Part(PrimitiveType.Cube, _figure, new Vector3(-0.18f, 1.2f, -0.74f), new Vector3(0.1f, 0.12f, 1.1f), steel, new Vector3(-55f, 0f, -30f));
            _gear[EquipmentSlot.Pistol] = Part(PrimitiveType.Cube, _figure, new Vector3(0.46f, 0.8f, 0f), new Vector3(0.08f, 0.22f, 0.3f), steel);
            _gear[EquipmentSlot.Melee] = Part(PrimitiveType.Cube, _figure, new Vector3(-0.44f, 0.82f, 0.12f), new Vector3(0.05f, 0.34f, 0.08f), steel);

            _camera = new GameObject("Preview Camera").AddComponent<Camera>();
            _camera.transform.SetParent(_stage, false);
            _camera.transform.localPosition = new Vector3(0f, 1.1f, 4.2f);
            _camera.transform.localRotation = Quaternion.Euler(4f, 180f, 0f);
            _camera.fieldOfView = 32f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.07f, 0.08f, 0.09f, 1f);
            _camera.cullingMask = 1 << _layer;
            _camera.depth = -10f;
            _camera.targetTexture = _texture;

            SetLayer(_stage, _layer);
            Show(null);
        }

        private void DestroyStage()
        {
            _gear.Clear();
            if (_camera != null) _camera.targetTexture = null;
            if (_stage != null) Destroy(_stage.gameObject);
            foreach (Material material in _materials)
                if (material != null) Destroy(material);
            _materials.Clear();
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
            _texture = null;
            _camera = null;
            _stage = null;
            _figure = null;
        }

        private static GameObject Part(PrimitiveType shape, Transform parent, Vector3 position, Vector3 scale, Material material,
            Vector3 euler = default)
        {
            GameObject part = GameObject.CreatePrimitive(shape);
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = Quaternion.Euler(euler.x, euler.y, euler.z);
            part.transform.localScale = scale;
            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;
            return part;
        }

        private Material Tint(Material source, Color color)
        {
            if (source == null) return null;
            var material = new Material(source) { color = color };
            _materials.Add(material);
            return material;
        }

        private static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root) SetLayer(child, layer);
        }
    }
}
