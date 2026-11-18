using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Hideout
{
    /// <summary>
    /// The hideout's facilities: stash size levels bought with credits, the workbench's recipes and queue size, and
    /// how many gun bench presets a player may keep. Server and clients must use the same asset.
    /// </summary>
    [CreateAssetMenu(fileName = "Hideout", menuName = "Extraction Shooter/Hideout")]
    public sealed class HideoutDefinition : ScriptableObject
    {
        [Tooltip("Stash sizes in upgrade order. The first is the free stash every player starts with (its price is ignored). " +
                 "Keep the width the same and only add rows, so upgrades never move items.")]
        [SerializeField] private List<StashTier> _stashTiers = new List<StashTier> { StashTier.Default };

        [SerializeField] private List<CraftRecipeDefinition> _recipes = new List<CraftRecipeDefinition>();

        [Tooltip("Crafts that can run at the same time.")]
        [SerializeField, Min(1)] private int _craftSlots = 2;

        [Tooltip("Gun bench presets a player can save.")]
        [SerializeField, Min(1)] private int _maxPresets = 10;

        private long[] _stashPrices;

        public IReadOnlyList<StashTier> StashTiers => _stashTiers;
        public IReadOnlyList<CraftRecipeDefinition> Recipes => _recipes;
        public int CraftSlots => Mathf.Max(1, _craftSlots);
        public int MaxPresets => Mathf.Max(1, _maxPresets);

        /// <summary>Stash level prices, for the upgrade rules.</summary>
        public IReadOnlyList<long> StashPrices
        {
            get
            {
                if (_stashPrices == null || _stashPrices.Length != _stashTiers.Count)
                {
                    _stashPrices = new long[_stashTiers.Count];
                    for (int i = 0; i < _stashTiers.Count; i++) _stashPrices[i] = _stashTiers[i].Price;
                }
                return _stashPrices;
            }
        }

        public bool TryGetRecipe(int index, out CraftRecipeDefinition recipe)
        {
            recipe = index >= 0 && index < _recipes.Count ? _recipes[index] : null;
            return recipe != null && recipe.Recipe.IsValid;
        }

        public bool TryGetRecipe(string id, out CraftRecipeDefinition recipe)
        {
            recipe = null;
            if (string.IsNullOrEmpty(id)) return false;
            foreach (CraftRecipeDefinition candidate in _recipes)
            {
                if (candidate == null || candidate.Id != id || !candidate.Recipe.IsValid) continue;
                recipe = candidate;
                return true;
            }
            return false;
        }

        private void OnEnable() => _stashPrices = null;

        private void OnValidate()
        {
            _stashPrices = null;
            var ids = new HashSet<string>();
            foreach (CraftRecipeDefinition recipe in _recipes)
                if (recipe != null && !string.IsNullOrEmpty(recipe.Id) && !ids.Add(recipe.Id))
                    Debug.LogError($"[{nameof(HideoutDefinition)}] Duplicate recipe id '{recipe.Id}'.", this);

            for (int i = 1; i < _stashTiers.Count; i++)
                if (_stashTiers[i].Width < _stashTiers[i - 1].Width || _stashTiers[i].Height < _stashTiers[i - 1].Height)
                    Debug.LogWarning($"[{nameof(HideoutDefinition)}] '{name}': stash level {i} is smaller than the one before it.", this);
        }
    }

    /// <summary>One stash size and what upgrading to it costs.</summary>
    [Serializable]
    public struct StashTier
    {
        [SerializeField, Min(1)] private int _width;
        [SerializeField, Min(1)] private int _height;
        [SerializeField, Min(0)] private long _price;

        public StashTier(int width, int height, long price)
        {
            _width = width;
            _height = height;
            _price = price;
        }

        /// <summary>The free stash: 10 x 12 cells.</summary>
        public static StashTier Default => new StashTier(10, 12, 0);

        public int Width => Mathf.Max(1, _width);
        public int Height => Mathf.Max(1, _height);
        public int Cells => Width * Height;
        public long Price => _price;
    }
}
