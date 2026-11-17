using ExtractionShooter.Gameplay.Weapons;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>The player's equipment slots. Values are slot indices in the equipment container.</summary>
    public enum EquipmentSlot : byte
    {
        Helmet = 0,
        BodyArmor = 1,
        ChestRig = 2,
        Backpack = 3,
        Primary1 = 4,
        Primary2 = 5,
        Pistol = 6,
    }

    /// <summary>Which item types each equipment slot accepts.</summary>
    public static class EquipmentRules
    {
        public const int SlotCount = 7;

        public static readonly EquipmentSlot[] WeaponSlots =
            { EquipmentSlot.Primary1, EquipmentSlot.Primary2, EquipmentSlot.Pistol };

        public static bool Accepts(EquipmentSlot slot, ItemDefinition item)
        {
            switch (slot)
            {
                case EquipmentSlot.Helmet:
                    return item is ArmorDefinition helmet && helmet.ArmorSlot == ArmorSlot.Helmet;
                case EquipmentSlot.BodyArmor:
                    return item is ArmorDefinition armor && armor.ArmorSlot == ArmorSlot.BodyArmor;
                case EquipmentSlot.ChestRig:
                    return item is ContainerItemDefinition rig && rig.ContainerKind == ContainerKind.ChestRig;
                case EquipmentSlot.Backpack:
                    return item is ContainerItemDefinition bag && bag.ContainerKind == ContainerKind.Backpack;
                case EquipmentSlot.Primary1:
                case EquipmentSlot.Primary2:
                    return item is WeaponDefinition primary && primary.WeaponClass == WeaponClass.Primary;
                case EquipmentSlot.Pistol:
                    return item is WeaponDefinition pistol && pistol.WeaponClass == WeaponClass.Pistol;
                default:
                    return false;
            }
        }

        public static bool IsWeaponSlot(EquipmentSlot slot) =>
            slot == EquipmentSlot.Primary1 || slot == EquipmentSlot.Primary2 || slot == EquipmentSlot.Pistol;

        public static string DisplayName(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.BodyArmor: return "Body Armor";
                case EquipmentSlot.ChestRig: return "Chest Rig";
                case EquipmentSlot.Primary1: return "Primary 1";
                case EquipmentSlot.Primary2: return "Primary 2";
                default: return slot.ToString();
            }
        }
    }
}
