using ExtractionShooter.Core.Raid;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Raid;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Inventory
{
    /// <summary>Top-centre raid HUD: difficulty and time left.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class RaidHudView : MonoBehaviour
    {
        private const string HiddenClass = "hidden";
        private const string WarningClass = "raid-hud--warning";
        private const int WarningSeconds = 120;

        private Label _label;
        private PlayerRaid _raid;

        private void OnEnable()
        {
            _label = GetComponent<UIDocument>().rootVisualElement.Q<Label>("raid-hud");
            if (_label == null)
            {
                Debug.LogError($"[{nameof(RaidHudView)}] UXML is missing 'raid-hud'.", this);
                enabled = false;
                return;
            }

            PlayerCharacter.LocalPlayerChanged += Bind;
            Bind(PlayerCharacter.Local);
        }

        private void OnDisable()
        {
            PlayerCharacter.LocalPlayerChanged -= Bind;
            Bind(null);
        }

        private void Bind(PlayerCharacter player)
        {
            if (_raid != null) _raid.Changed -= Refresh;
            _raid = player != null ? player.Raid : null;
            if (_raid != null) _raid.Changed += Refresh;
            Refresh();
        }

        private void Refresh()
        {
            _label.EnableInClassList(HiddenClass, _raid == null);
            if (_raid == null) return;

            int seconds = Mathf.Max(0, _raid.SecondsLeft);
            string time = $"{_raid.Difficulty.DisplayName().ToUpperInvariant()}   {seconds / 60:00}:{seconds % 60:00}";
            _label.text = time; // extraction has its own centre prompt (ExtractHudView)
            _label.EnableInClassList(WarningClass, seconds <= WarningSeconds);
        }
    }
}
