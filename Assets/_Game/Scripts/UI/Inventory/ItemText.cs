using System.Text;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Weapons;

namespace ExtractionShooter.UI.Inventory
{
    /// <summary>Formats item stats for slots, the details line and the HUD, so all three read the same.</summary>
    public static class ItemText
    {
        /// <summary>Short text under an item in a slot.</summary>
        public static string SlotDetail(ItemDefinition definition, in ItemStack stack)
        {
            switch (definition)
            {
                case WeaponDefinition weapon:
                    return stack.AmmoCount > 0 ? $"{stack.AmmoCount}/{weapon.MagazineSize}" : "Empty";
                case AmmoDefinition ammo:
                    return CaliberNames.Display(ammo.Caliber);
                case ArmorDefinition armor:
                    return stack.Durability > 0 ? $"C{armor.ArmorClass}  {stack.Durability}/{armor.MaxDurability}" : $"C{armor.ArmorClass}  Broken";
                case ContainerItemDefinition bag:
                    return $"{bag.Capacity} slots";
                case MedicalDefinition medical:
                    return $"+{medical.HealAmount} HP";
                default:
                    return string.Empty;
            }
        }

        /// <summary>One-line description with stats, for the inventory details bar.</summary>
        public static string Details(ItemDefinition definition, in ItemStack stack, ItemDatabase database)
        {
            var text = new StringBuilder(definition.DisplayName);
            switch (definition)
            {
                case WeaponDefinition weapon:
                    text.Append("  —  ").Append(CaliberNames.Display(weapon.Caliber))
                        .Append("  ·  ").Append(weapon.MagazineSize).Append(" rds")
                        .Append("  ·  ").Append(weapon.RoundsPerMinute.ToString("0")).Append(" RPM")
                        .Append("  ·  ").Append(weapon.FireMode == FireMode.FullAuto ? "Auto" : "Semi")
                        .Append("  ·  Loaded: ").Append(LoadedText(weapon, stack, database));
                    break;
                case AmmoDefinition ammo:
                    text.Append("  —  ").Append(CaliberNames.Display(ammo.Caliber))
                        .Append("  ·  Damage ").Append(ammo.Damage);
                    if (ammo.Projectiles > 1) text.Append(" x").Append(ammo.Projectiles);
                    text.Append("  ·  Penetration ").Append(ammo.Penetration.ToString("0"))
                        .Append("  ·  Armor damage ").Append(ammo.ArmorDamagePercent.ToString("0")).Append('%');
                    break;
                case ArmorDefinition armor:
                    text.Append("  —  Class ").Append(armor.ArmorClass)
                        .Append("  ·  ").Append(stack.Durability > 0 ? $"{stack.Durability}/{armor.MaxDurability}" : "Broken");
                    break;
                case MedicalDefinition medical:
                    text.Append("  —  Heals ").Append(medical.HealAmount).Append("   (right-click to use)");
                    break;
                default:
                    if (!string.IsNullOrEmpty(definition.Description)) text.Append("  —  ").Append(definition.Description);
                    break;
            }
            if (stack.HasContents) text.Append("   (right-click to open)");
            return text.ToString();
        }

        /// <summary>"M855 24/30", or "Empty".</summary>
        public static string LoadedText(WeaponDefinition weapon, in ItemStack stack, ItemDatabase database)
        {
            if (stack.AmmoCount <= 0) return $"Empty 0/{weapon.MagazineSize}";
            string ammoName = database != null && database.TryGet(stack.AmmoId, out ItemDefinition ammo) ? ammo.DisplayName : stack.AmmoId;
            return $"{ammoName} {stack.AmmoCount}/{weapon.MagazineSize}";
        }
    }
}
