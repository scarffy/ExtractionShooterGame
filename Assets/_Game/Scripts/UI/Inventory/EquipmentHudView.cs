using System.Text;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Weapons;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Inventory
{
    /// <summary>
    /// Always-on HUD: loaded rounds of the drawn weapon (and reload progress), weapon slots
    /// (active one marked) and helmet/body armor class and durability.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class EquipmentHudView : MonoBehaviour
    {
        private const string HiddenClass = "hidden";

        private VisualElement _hud;
        private Label _ammo;
        private Label _weapons;
        private Label _armor;
        private PlayerCharacter _player;
        private readonly StringBuilder _builder = new StringBuilder();

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _hud = root.Q<VisualElement>("equipment-hud");
            _ammo = root.Q<Label>("ammo-hud");
            _weapons = root.Q<Label>("weapon-hud");
            _armor = root.Q<Label>("armor-hud");

            if (_hud == null || _ammo == null || _weapons == null || _armor == null)
            {
                Debug.LogError($"[{nameof(EquipmentHudView)}] UXML is missing the equipment HUD elements.", this);
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

        private void Update()
        {
            // Only the reload progress changes without an event.
            if (_player != null && _player.Reloader != null && _player.Reloader.IsReloading) RefreshAmmo();
        }

        private void Bind(PlayerCharacter player)
        {
            if (_player != null)
            {
                if (_player.Equipment != null) _player.Equipment.Changed -= Refresh;
                if (_player.Reloader != null) _player.Reloader.ReloadStateChanged -= Refresh;
            }

            _player = player;

            if (_player != null)
            {
                if (_player.Equipment != null) _player.Equipment.Changed += Refresh;
                if (_player.Reloader != null) _player.Reloader.ReloadStateChanged += Refresh;
            }
            Refresh();
        }

        private void Refresh()
        {
            PlayerEquipment equipment = _player != null ? _player.Equipment : null;
            _hud.EnableInClassList(HiddenClass, equipment == null);
            if (equipment == null) return;

            RefreshAmmo();

            _builder.Clear();
            for (int i = 0; i < EquipmentRules.WeaponSlots.Length; i++)
            {
                EquipmentSlot slot = EquipmentRules.WeaponSlots[i];
                string name = equipment.TryGetDefinition(slot, out WeaponDefinition weapon) ? weapon.DisplayName : "—";
                bool active = equipment.ActiveWeaponSlot == slot;
                if (i > 0) _builder.Append("     ");
                _builder.Append(active ? "▶ " : "  ").Append(i + 1).Append(' ').Append(name);
            }
            _weapons.text = _builder.ToString();

            _armor.text = $"Helmet {ArmorText(equipment, EquipmentSlot.Helmet)}     Armor {ArmorText(equipment, EquipmentSlot.BodyArmor)}";
        }

        private void RefreshAmmo()
        {
            PlayerEquipment equipment = _player != null ? _player.Equipment : null;
            if (equipment == null || !equipment.TryGetActiveWeapon(out EquipmentSlot _, out WeaponDefinition weapon, out ItemStack item))
            {
                _ammo.text = string.Empty;
                return;
            }

            WeaponReloader reloader = _player.Reloader;
            _ammo.text = reloader != null && reloader.IsReloading
                ? $"{weapon.DisplayName}   Reloading {Mathf.RoundToInt(reloader.Progress * 100f)}%"
                : $"{weapon.DisplayName}   {ItemText.LoadedText(weapon, item, equipment.Container.ItemDatabase)}";
        }

        private static string ArmorText(PlayerEquipment equipment, EquipmentSlot slot)
        {
            if (!equipment.TryGetDefinition(slot, out ArmorDefinition armor)) return "—";
            ItemStack piece = equipment.GetItem(slot);
            return piece.Durability > 0 ? $"C{armor.ArmorClass} {piece.Durability}/{armor.MaxDurability}" : $"C{armor.ArmorClass} broken";
        }
    }
}
