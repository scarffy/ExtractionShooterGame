using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Weapons;

namespace ExtractionShooter.Gameplay.Visuals
{
    /// <summary>
    /// Picks the model for an item: a <see cref="VisualProfile"/> override for its id first, otherwise a default from
    /// its type and numbers (armor class, bag size, weapon class, caliber, optics, attachment slot).
    /// </summary>
    public static class VisualClassifier
    {
        /// <summary>Armor class from which helmets and body armor use the heavy model.</summary>
        public const int HeavyArmorClass = 4;

        /// <summary>Storage cells from which a chest rig uses the large model.</summary>
        public const int LargeRigCells = 8;

        /// <summary>Storage cells from which a backpack uses the large model.</summary>
        public const int LargeBackpackCells = 18;

        /// <summary>Bolt-action territory: scoped weapons slower than this are drawn as sniper rifles.</summary>
        public const float SniperMaxRoundsPerMinute = 120f;

        public static GearShape Of(ItemDefinition item, VisualProfile profile)
        {
            if (item == null) return GearShape.None;
            if (profile != null && profile.TryGetItem(item.Id, out ItemVisual visual) && visual.Shape != GearShape.None)
                return visual.Shape;

            switch (item)
            {
                case WeaponDefinition weapon: return WeaponShape(weapon);
                case AttachmentDefinition attachment: return AttachmentShape(attachment);
                case ArmorDefinition armor:
                    bool heavy = armor.ArmorClass >= HeavyArmorClass;
                    if (armor.ArmorSlot == ArmorSlot.Helmet) return heavy ? GearShape.HelmetHeavy : GearShape.HelmetLight;
                    return heavy ? GearShape.PlateCarrier : GearShape.SoftVest;
                case ContainerItemDefinition bag:
                    if (bag.ContainerKind == ContainerKind.ChestRig) return bag.Capacity >= LargeRigCells ? GearShape.RigLarge : GearShape.RigSmall;
                    return bag.Capacity >= LargeBackpackCells ? GearShape.BackpackLarge : GearShape.BackpackSmall;
                case MeleeDefinition _:
                    return GearShape.Knife;
                default:
                    return GearShape.None;
            }
        }

        public static GearShape WeaponShape(WeaponDefinition weapon)
        {
            if (weapon.WeaponClass == WeaponClass.Pistol) return GearShape.Pistol;
            if (weapon.Caliber == Caliber.Gauge12) return GearShape.Shotgun;
            if (weapon.Scoped) return weapon.RoundsPerMinute < SniperMaxRoundsPerMinute ? GearShape.Sniper : GearShape.Marksman;
            switch (weapon.Caliber)
            {
                case Caliber.Mm9x19:
                case Caliber.Acp45:
                case Caliber.Mm46x30:
                case Caliber.Ae50:
                    return GearShape.Smg;
                default:
                    return GearShape.Rifle;
            }
        }

        public static GearShape AttachmentShape(AttachmentDefinition attachment)
        {
            switch (attachment.Slot)
            {
                case AttachmentSlot.Optic: return attachment.Scoped ? GearShape.Scope : GearShape.RedDot;
                case AttachmentSlot.Muzzle: return attachment.SoundRangeMultiplier < 1f ? GearShape.Suppressor : GearShape.Compensator;
                case AttachmentSlot.Grip: return GearShape.VerticalGrip;
                case AttachmentSlot.Magazine: return GearShape.ExtendedMag;
                default: return GearShape.None;
            }
        }

        /// <summary>How a weapon shape is held.</summary>
        public static HoldStyle HoldOf(GearShape shape)
        {
            switch (shape)
            {
                case GearShape.Pistol: return HoldStyle.Pistol;
                case GearShape.Smg:
                case GearShape.Rifle:
                case GearShape.Shotgun:
                case GearShape.Marksman:
                case GearShape.Sniper:
                    return HoldStyle.Long;
                default:
                    return HoldStyle.None;
            }
        }
    }
}
