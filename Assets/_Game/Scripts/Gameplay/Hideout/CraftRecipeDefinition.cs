using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Hideout;
using ExtractionShooter.Gameplay.Inventory;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Hideout
{
    /// <summary>
    /// One workbench recipe: items taken from the stash, the item made, the real time it takes and an optional credit
    /// cost. Listed on the <see cref="HideoutDefinition"/>; the id is saved with queued crafts, so never change it.
    /// </summary>
    [CreateAssetMenu(fileName = "Recipe", menuName = "Extraction Shooter/Craft Recipe")]
    public sealed class CraftRecipeDefinition : ScriptableObject
    {
        [Tooltip("Stable id saved in profiles with queued crafts. Never rename.")]
        [SerializeField] private string _recipeId;

        [Tooltip("Shown in the recipe list. Empty uses the output's name.")]
        [SerializeField] private string _displayName;

        [Tooltip("Taken from plain stacks in the stash when the craft starts.")]
        [SerializeField] private List<RecipeInput> _inputs = new List<RecipeInput>();

        [SerializeField] private ItemDefinition _output;

        [Tooltip("Items made (clamped to the output's max stack).")]
        [SerializeField, Min(1)] private int _outputQuantity = 1;

        [Tooltip("Real seconds from starting the craft until it can be collected. Keeps running while offline.")]
        [SerializeField, Min(0)] private int _craftSeconds = 60;

        [Tooltip("Credits charged when the craft starts.")]
        [SerializeField, Min(0)] private long _creditCost;

        private CraftRecipe _recipe;

        public string Id => _recipeId;
        public string DisplayName => !string.IsNullOrEmpty(_displayName) ? _displayName : _output != null ? _output.DisplayName : name;
        public IReadOnlyList<RecipeInput> Inputs => _inputs;
        public ItemDefinition Output => _output;
        public int OutputQuantity => _output != null ? Mathf.Clamp(_outputQuantity, 1, _output.MaxStack) : 1;
        public int CraftSeconds => Mathf.Max(0, _craftSeconds);
        public long CreditCost => Math.Max(0L, _creditCost);

        /// <summary>The recipe as plain data for the crafting rules (cached).</summary>
        public CraftRecipe Recipe => _recipe ?? (_recipe = Build());

        private CraftRecipe Build()
        {
            var inputs = new List<ItemAmount>(_inputs.Count);
            foreach (RecipeInput input in _inputs)
                if (input != null && input.Item != null) inputs.Add(new ItemAmount(input.Item.Id, input.Quantity));
            var output = new ItemAmount(_output != null ? _output.Id : null, OutputQuantity);
            return new CraftRecipe(_recipeId, inputs, output, CraftSeconds, CreditCost);
        }

        private void OnEnable() => _recipe = null;

        private void OnValidate()
        {
            _recipe = null;
            if (string.IsNullOrEmpty(_recipeId)) Debug.LogWarning($"[{nameof(CraftRecipeDefinition)}] '{name}' has no id.", this);
            if (_output == null) Debug.LogWarning($"[{nameof(CraftRecipeDefinition)}] '{name}' has no output.", this);
        }
    }

    /// <summary>One input of a recipe.</summary>
    [Serializable]
    public sealed class RecipeInput
    {
        [SerializeField] private ItemDefinition _item;
        [SerializeField, Min(1)] private int _quantity = 1;

        public ItemDefinition Item => _item;
        public int Quantity => Mathf.Max(1, _quantity);
    }
}
