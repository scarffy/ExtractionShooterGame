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
        public static string SlotDetail(ItemDefinition definition, in ItemStack stack, ItemDatabase database = null)
        {
            switch (definition)
            {
                case WeaponDefinition weapon:
                    string mods = string.IsNullOrEmpty(stack.Attachments) ? string.Empty : " +";
                    return (stack.AmmoCount > 0 ? $"{stack.AmmoCount}/{MagazineOf(weapon, stack, database)}" : "Empty") + mods;
                case AttachmentDefinition mod:
                    return SlotName(mod.Slot);
                case AmmoDefinition ammo:
                    return CaliberNames.Display(ammo.Caliber);
                case ArmorDefinition armor:
                    return stack.Durability > 0 ? $"C{armor.ArmorClass}  {stack.Durability}/{armor.MaxDurability}" : $"C{armor.ArmorClass}  Broken";
                case ContainerItemDefinition bag:
                    return $"{bag.GridWidth}x{bag.GridHeight}";
                case MedicalDefinition medical:
                    return $"+{medical.HealAmount} HP";
                case KeyDefinition key:
                    return $"{key.UsesLeft(stack)}/{key.MaxUses}";
                default:
                    return string.Empty;
            }
        }

        /// <summary>One-line description with rarity and stats, for the inventory details bar (rich text).</summary>
        public static string Details(ItemDefinition definition, in ItemStack stack, ItemDatabase database)
        {
            var text = new StringBuilder(definition.DisplayName);
            text.Append("  [").Append(RarityStyle.RichName(definition.Rarity)).Append(']');
            switch (definition)
            {
                case WeaponDefinition weapon:
                    text.Append("  —  ").Append(CaliberNames.Display(weapon.Caliber))
                        .Append("  ·  ").Append(MagazineOf(weapon, stack, database)).Append(" rds")
                        .Append("  ·  ").Append(weapon.RoundsPerMinute.ToString("0")).Append(" RPM")
                        .Append("  ·  ").Append(weapon.FireMode == FireMode.FullAuto ? "Auto" : "Semi")
                        .Append("  ·  Loaded: ").Append(LoadedText(weapon, stack, database));
                    AppendAttachments(text, stack, database);
                    break;
                case AttachmentDefinition mod:
                    text.Append("  —  ").Append(SlotName(mod.Slot)).Append(" attachment");
                    AppendModifiers(text, mod);
                    text.Append("   (drag onto a weapon to fit)");
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
                case KeyDefinition key:
                    text.Append("  —  Uses ").Append(key.UsesLeft(stack)).Append('/').Append(key.MaxUses);
                    if (!string.IsNullOrEmpty(definition.Description)) text.Append("  ·  ").Append(definition.Description);
                    text.Append("   (carry it to unlock its door)");
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
            int magazine = MagazineOf(weapon, stack, database);
            if (stack.AmmoCount <= 0) return $"Empty 0/{magazine}";
            string ammoName = database != null && database.TryGet(stack.AmmoId, out ItemDefinition ammo) ? ammo.DisplayName : stack.AmmoId;
            return $"{ammoName} {stack.AmmoCount}/{magazine}";
        }

        public static string SlotName(AttachmentSlot slot)
        {
            switch (slot)
            {
                case AttachmentSlot.Optic: return "Optic";
                case AttachmentSlot.Muzzle: return "Muzzle";
                case AttachmentSlot.Grip: return "Grip";
                case AttachmentSlot.Magazine: return "Magazine";
                default: return slot.ToString();
            }
        }

        private static int MagazineOf(WeaponDefinition weapon, in ItemStack stack, ItemDatabase database) =>
            WeaponStats.Of(weapon, stack, database).MagazineSize;

        private static void AppendAttachments(StringBuilder text, in ItemStack stack, ItemDatabase database)
        {
            if (string.IsNullOrEmpty(stack.Attachments)) return;
            text.Append("  ·  Mods: ");
            bool first = true;
            foreach (var fitted in WeaponMods.All(stack.Attachments))
            {
                if (!first) text.Append(", ");
                first = false;
                text.Append(database != null && database.TryGet(fitted.Value, out ItemDefinition mod) ? mod.DisplayName : fitted.Value);
            }
            text.Append("   (right-click to remove the last)");
        }

        private static void AppendModifiers(StringBuilder text, AttachmentDefinition mod)
        {
            if (mod.AimFieldOfView > 0f) text.Append("  ·  ").Append(mod.Scoped ? "Scope " : "Sight ").Append(mod.AimFieldOfView.ToString("0")).Append("° FOV");
            AppendPercent(text, "Spread", mod.SpreadMultiplier);
            AppendPercent(text, "Hip spread", mod.HipSpreadMultiplier);
            AppendPercent(text, "Aim time", mod.AimSecondsMultiplier);
            AppendPercent(text, "Sound", mod.SoundRangeMultiplier);
            if (mod.MagazineBonus > 0) text.Append("  ·  +").Append(mod.MagazineBonus).Append(" rds");
        }

        private static void AppendPercent(StringBuilder text, string label, float multiplier)
        {
            if (UnityEngine.Mathf.Approximately(multiplier, 1f)) return;
            int change = UnityEngine.Mathf.RoundToInt((multiplier - 1f) * 100f);
            text.Append("  ·  ").Append(label).Append(' ').Append(change > 0 ? "+" : string.Empty).Append(change).Append('%');
        }
    }
}
