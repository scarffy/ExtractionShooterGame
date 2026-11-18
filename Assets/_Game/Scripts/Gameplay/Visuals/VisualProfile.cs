using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Visuals
{
    /// <summary>
    /// Look of a faction's characters and their gear: base materials, uniform and gear colours, weapon colours and
    /// per-item overrides (shape and tint by item id). Players, bots and the lobby preview are all built from one of
    /// these by <see cref="CharacterBody"/>, so a new faction is a new asset, not new code.
    /// </summary>
    [CreateAssetMenu(fileName = "VisualProfile", menuName = "Extraction Shooter/Visual Profile")]
    public sealed class VisualProfile : ScriptableObject
    {
        [Header("Materials")]
        [Tooltip("Lit base material (URP Lit). Tinted per part; instances are shared per colour.")]
        [SerializeField] private Material _litMaterial;

        [Tooltip("Unlit base material for glowing parts: sight reticles and muzzle flashes.")]
        [SerializeField] private Material _glowMaterial;

        [Header("Body")]
        [SerializeField] private Color _skin = new Color(0.78f, 0.6f, 0.48f);
        [SerializeField] private Color _top = new Color(0.33f, 0.37f, 0.27f);
        [SerializeField] private Color _bottom = new Color(0.55f, 0.5f, 0.38f);
        [SerializeField] private Color _boots = new Color(0.22f, 0.19f, 0.15f);
        [SerializeField] private Color _gloves = new Color(0.16f, 0.16f, 0.15f);

        [Tooltip("Knee pads, belt and straps.")]
        [SerializeField] private Color _pads = new Color(0.2f, 0.21f, 0.18f);

        [Header("Head")]
        [Tooltip("Without a helmet: wear a cap/beanie (true) or show bare head with short hair (false).")]
        [SerializeField] private bool _capWhenBareheaded;
        [SerializeField] private Color _cap = new Color(0.25f, 0.27f, 0.22f);
        [SerializeField] private Color _hair = new Color(0.16f, 0.12f, 0.09f);
        [SerializeField] private Color _eyewear = new Color(0.06f, 0.06f, 0.07f);

        [Tooltip("Lower face covered by a balaclava or face mask in this colour (alpha 0: no mask).")]
        [SerializeField] private Color _mask = new Color(0f, 0f, 0f, 0f);

        [Header("Gear")]
        [SerializeField] private Color _helmet = new Color(0.4f, 0.42f, 0.32f);
        [SerializeField] private Color _armor = new Color(0.36f, 0.38f, 0.29f);
        [SerializeField] private Color _rig = new Color(0.46f, 0.42f, 0.32f);
        [SerializeField] private Color _pouch = new Color(0.4f, 0.37f, 0.28f);
        [SerializeField] private Color _backpack = new Color(0.31f, 0.33f, 0.25f);

        [Header("Weapons")]
        [SerializeField] private Color _gunMetal = new Color(0.13f, 0.13f, 0.14f);
        [SerializeField] private Color _polymer = new Color(0.2f, 0.2f, 0.19f);
        [SerializeField] private Color _opticGlass = new Color(0.12f, 0.2f, 0.22f);
        [SerializeField] private Color _reticle = new Color(1f, 0.15f, 0.1f);
        [SerializeField] private Color _muzzleFlash = new Color(1f, 0.8f, 0.4f);

        [Header("Per-item overrides")]
        [Tooltip("Shape and/or tint per item id. Items not listed use the defaults from their type and stats.")]
        [SerializeField] private List<ItemVisual> _items = new List<ItemVisual>();

        public Color Skin => _skin;
        public Color Top => _top;
        public Color Bottom => _bottom;
        public Color Boots => _boots;
        public Color Gloves => _gloves;
        public Color Pads => _pads;
        public bool CapWhenBareheaded => _capWhenBareheaded;
        public Color Cap => _cap;
        public Color Hair => _hair;
        public Color Eyewear => _eyewear;
        public Color Mask => _mask;
        public bool HasMask => _mask.a > 0.01f;
        public Color Helmet => _helmet;
        public Color Armor => _armor;
        public Color Rig => _rig;
        public Color Pouch => _pouch;
        public Color Backpack => _backpack;
        public Color GunMetal => _gunMetal;
        public Color Polymer => _polymer;
        public Color OpticGlass => _opticGlass;
        public Color Reticle => _reticle;
        public Color MuzzleFlash => _muzzleFlash;

        /// <summary>The lit base material tinted <paramref name="color"/> (shared instance).</summary>
        public Material Lit(Color color) => TintCache.Get(_litMaterial, color);

        /// <summary>The unlit base material tinted <paramref name="color"/> (shared instance).</summary>
        public Material Glow(Color color) => TintCache.Get(_glowMaterial != null ? _glowMaterial : _litMaterial, color);

        /// <summary>The override for an item id, if the profile has one.</summary>
        public bool TryGetItem(string itemId, out ItemVisual visual)
        {
            visual = null;
            if (string.IsNullOrEmpty(itemId) || _items == null) return false;
            foreach (ItemVisual entry in _items)
            {
                if (entry != null && string.Equals(entry.ItemId, itemId, StringComparison.Ordinal))
                {
                    visual = entry;
                    return true;
                }
            }
            return false;
        }

        /// <summary>The tint set for an item id, or <paramref name="fallback"/>.</summary>
        public Color TintOr(string itemId, Color fallback) =>
            TryGetItem(itemId, out ItemVisual visual) && visual.HasTint ? visual.Tint : fallback;

        private void OnValidate()
        {
            if (_litMaterial == null)
                Debug.LogWarning($"[{nameof(VisualProfile)}] '{name}' has no lit material.", this);
        }
    }

    /// <summary>A per-item override in a <see cref="VisualProfile"/>.</summary>
    [Serializable]
    public sealed class ItemVisual
    {
        [SerializeField] private string _itemId;

        [Tooltip("None keeps the default shape for the item's type.")]
        [SerializeField] private GearShape _shape;

        [Tooltip("Main colour of the item (weapons: furniture such as stock and handguard). Alpha 0 keeps the profile colour.")]
        [SerializeField] private Color _tint = new Color(0f, 0f, 0f, 0f);

        public string ItemId => _itemId;
        public GearShape Shape => _shape;
        public Color Tint => _tint;
        public bool HasTint => _tint.a > 0.01f;
    }

    /// <summary>
    /// Shared tinted material instances: one per (base material, colour), so every character with the same colours
    /// uses the same materials and they batch. Cleared when play mode starts.
    /// </summary>
    public static class TintCache
    {
        private static readonly Dictionary<Material, Dictionary<int, Material>> Materials =
            new Dictionary<Material, Dictionary<int, Material>>();

        /// <summary>A material like <paramref name="source"/> with its colour set; null if the source is null.</summary>
        public static Material Get(Material source, Color color)
        {
            if (source == null) return null;
            if (!Materials.TryGetValue(source, out Dictionary<int, Material> tints))
            {
                tints = new Dictionary<int, Material>();
                Materials[source] = tints;
            }

            int key = Pack(color);
            if (tints.TryGetValue(key, out Material material) && material != null) return material;

            material = new Material(source) { name = source.name + " (tint)", color = color };
            tints[key] = material;
            return material;
        }

        private static int Pack(Color color)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            int a = Mathf.Clamp(Mathf.RoundToInt(color.a * 255f), 0, 255);
            return (r << 24) | (g << 16) | (b << 8) | a;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Materials.Clear();
    }
}
