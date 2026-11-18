using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Inventory;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Inventory
{
    /// <summary>The "17.3 / 70 kg" readout under the character, tinted when heavy or overloaded.</summary>
    internal static class WeightLabel
    {
        private const string HeavyClass = "character-weight--heavy";
        private const string OverloadedClass = "character-weight--overloaded";

        public static void Show(Label label, float kilograms, Encumbrance rules)
        {
            if (label == null) return;
            label.text = GearWeight.Format(kilograms, rules.MaxLoad);
            LoadLevel level = rules.LevelOf(kilograms);
            label.EnableInClassList(HeavyClass, level == LoadLevel.Heavy || level == LoadLevel.VeryHeavy);
            label.EnableInClassList(OverloadedClass, level == LoadLevel.Overloaded);
        }
    }
}
