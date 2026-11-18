using System;
using ExtractionShooter.Core.Raid;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Hideout
{
    /// <summary>
    /// Two buttons in the hideout header for the map's two difficulties (Easy/Normal or Normal/Hard). Shows the current
    /// pick; clicking raises <see cref="Picked"/>. Disabled for squad members (the leader picks) and while searching.
    /// Plain C#: owned by <see cref="SquadPanel"/>.
    /// </summary>
    public sealed class DifficultyPicker : IDisposable
    {
        private const string ActiveClass = "difficulty-button--active";
        private const string HardClass = "difficulty-button--hard";
        private const string EasyClass = "difficulty-button--easy";

        private readonly VisualElement _root;
        private readonly Button _lower;
        private readonly Button _upper;
        private MapDifficulties _map;

        /// <summary>The player clicked a difficulty the map offers.</summary>
        public event Action<RaidDifficulty> Picked;

        public DifficultyPicker(VisualElement root)
        {
            _root = root;
            _lower = root?.Q<Button>("difficulty-lower");
            _upper = root?.Q<Button>("difficulty-upper");
            if (_lower != null) _lower.clicked += PickLower;
            if (_upper != null) _upper.clicked += PickUpper;
        }

        /// <param name="map">The map's two difficulties.</param>
        /// <param name="selected">The pick to highlight.</param>
        /// <param name="interactable">False for squad members and while searching.</param>
        public void Show(MapDifficulties map, RaidDifficulty selected, bool interactable)
        {
            _map = map;
            Style(_lower, map.Lower(), selected);
            Style(_upper, map.Upper(), selected);
            _root?.SetEnabled(interactable);
        }

        public void Dispose()
        {
            if (_lower != null) _lower.clicked -= PickLower;
            if (_upper != null) _upper.clicked -= PickUpper;
        }

        private static void Style(Button button, RaidDifficulty difficulty, RaidDifficulty selected)
        {
            if (button == null) return;
            button.text = difficulty.DisplayName();
            button.EnableInClassList(ActiveClass, difficulty == selected);
            button.EnableInClassList(EasyClass, difficulty == RaidDifficulty.Easy);
            button.EnableInClassList(HardClass, difficulty == RaidDifficulty.Hard);
        }

        private void PickLower() => Picked?.Invoke(_map.Lower());

        private void PickUpper() => Picked?.Invoke(_map.Upper());
    }
}
