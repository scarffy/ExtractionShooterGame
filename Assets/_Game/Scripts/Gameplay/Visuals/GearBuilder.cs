using UnityEngine;
using static ExtractionShooter.Gameplay.Visuals.PrimitiveParts;

namespace ExtractionShooter.Gameplay.Visuals
{
    /// <summary>
    /// Builds worn gear from primitives onto a <see cref="HumanoidRig"/>: helmets on the head, body armor, chest rig and
    /// backpack on the chest (each layer stacked outward from the last), the knife on the belt.
    /// Every piece is a single root transform, so swapping gear is destroying one root and building another.
    /// </summary>
    public static class GearBuilder
    {
        /// <summary>Chest-local Z of the front surface once body armor of this shape is worn.</summary>
        public static float FrontOf(GearShape armor) =>
            HumanoidRig.ChestFront + (armor == GearShape.PlateCarrier ? 0.055f : armor == GearShape.SoftVest ? 0.03f : 0f);

        /// <summary>Chest-local Z of the back surface once body armor of this shape is worn.</summary>
        public static float BackOf(GearShape armor) =>
            HumanoidRig.ChestBack - (armor == GearShape.PlateCarrier ? 0.055f : armor == GearShape.SoftVest ? 0.03f : 0f);

        public static Transform Helmet(Transform head, GearShape shape, Color color, VisualProfile profile)
        {
            Transform root = Node(head, "Helmet", Vector3.zero);
            Material shell = profile.Lit(color);
            Material dark = profile.Lit(profile.Pads);
            float y = HumanoidRig.HeadCenter;
            if (shape == GearShape.HelmetHeavy)
            {
                Sphere(root, "Shell", new Vector3(0f, y + 0.05f, -0.005f), new Vector3(0.255f, 0.22f, 0.275f), shell);
                Box(root, "RailL", new Vector3(-0.122f, y + 0.035f, 0f), new Vector3(0.016f, 0.025f, 0.13f), dark);
                Box(root, "RailR", new Vector3(0.122f, y + 0.035f, 0f), new Vector3(0.016f, 0.025f, 0.13f), dark);
                Cylinder(root, "EarL", new Vector3(-0.112f, y - 0.01f, 0.005f), 0.045f, 0.035f, AlongX, dark);
                Cylinder(root, "EarR", new Vector3(0.112f, y - 0.01f, 0.005f), 0.045f, 0.035f, AlongX, dark);
                Box(root, "NvgMount", new Vector3(0f, y + 0.1f, 0.125f), new Vector3(0.045f, 0.035f, 0.025f), dark);
                Box(root, "Counterweight", new Vector3(0f, y + 0.07f, -0.135f), new Vector3(0.07f, 0.05f, 0.03f), dark);
            }
            else
            {
                Sphere(root, "Shell", new Vector3(0f, y + 0.055f, -0.005f), new Vector3(0.235f, 0.2f, 0.255f), shell);
                Box(root, "Brim", new Vector3(0f, y + 0.012f, -0.005f), new Vector3(0.226f, 0.02f, 0.245f), shell);
                Box(root, "StrapL", new Vector3(-0.098f, y - 0.06f, 0.02f), new Vector3(0.008f, 0.1f, 0.02f), dark);
                Box(root, "StrapR", new Vector3(0.098f, y - 0.06f, 0.02f), new Vector3(0.008f, 0.1f, 0.02f), dark);
            }
            return root;
        }

        public static Transform Armor(Transform chest, GearShape shape, Color color, VisualProfile profile)
        {
            Transform root = Node(chest, "BodyArmor", Vector3.zero);
            Material armor = profile.Lit(color);
            Material dark = profile.Lit(profile.Pads);
            if (shape == GearShape.PlateCarrier)
            {
                float front = FrontOf(shape), back = BackOf(shape);
                Box(root, "FrontPlate", new Vector3(0f, 0.12f, front - 0.03f), new Vector3(0.32f, 0.31f, 0.06f), armor);
                Box(root, "BackPlate", new Vector3(0f, 0.12f, back + 0.03f), new Vector3(0.32f, 0.31f, 0.06f), armor);
                Box(root, "Cummerbund", new Vector3(0f, 0.01f, 0f), new Vector3(0.41f, 0.14f, front - back - 0.02f), armor);
                Box(root, "StrapL", new Vector3(-0.11f, 0.285f, 0f), new Vector3(0.075f, 0.025f, front - back), armor);
                Box(root, "StrapR", new Vector3(0.11f, 0.285f, 0f), new Vector3(0.075f, 0.025f, front - back), armor);
                Box(root, "Collar", new Vector3(0f, 0.28f, back + 0.025f), new Vector3(0.16f, 0.05f, 0.05f), dark);
            }
            else
            {
                float front = FrontOf(shape), back = BackOf(shape);
                Box(root, "Vest", new Vector3(0f, 0.11f, 0f), new Vector3(0.41f, 0.33f, front - back), armor);
                Box(root, "VestLower", new Vector3(0f, -0.08f, 0f), new Vector3(0.35f, 0.1f, front - back - 0.02f), armor);
                Box(root, "StrapL", new Vector3(-0.11f, 0.28f, 0f), new Vector3(0.07f, 0.03f, front - back - 0.01f), armor);
                Box(root, "StrapR", new Vector3(0.11f, 0.28f, 0f), new Vector3(0.07f, 0.03f, front - back - 0.01f), armor);
            }
            return root;
        }

        /// <summary>Chest rig: mag pouches across the front of whatever is worn underneath (<paramref name="armor"/>).</summary>
        public static Transform Rig(Transform chest, GearShape shape, GearShape armor, Color color, VisualProfile profile)
        {
            Transform root = Node(chest, "ChestRig", Vector3.zero);
            Material rig = profile.Lit(color);
            Material pouch = profile.Lit(profile.Pouch);
            float front = FrontOf(armor);
            float back = BackOf(armor);
            bool large = shape == GearShape.RigLarge;

            Box(root, "Panel", new Vector3(0f, 0.05f, front + 0.012f), new Vector3(large ? 0.36f : 0.3f, 0.16f, 0.024f), rig);
            Box(root, "HarnessL", new Vector3(-0.1f, 0.25f, (front + back) * 0.5f), new Vector3(0.045f, 0.02f, front - back + 0.02f), rig);
            Box(root, "HarnessR", new Vector3(0.1f, 0.25f, (front + back) * 0.5f), new Vector3(0.045f, 0.02f, front - back + 0.02f), rig);
            Box(root, "HarnessBack", new Vector3(0f, 0.15f, back - 0.008f), new Vector3(0.22f, 0.2f, 0.016f), rig);

            int pouches = large ? 4 : 3;
            float spacing = large ? 0.082f : 0.085f;
            for (int i = 0; i < pouches; i++)
            {
                float x = (i - (pouches - 1) * 0.5f) * spacing;
                Box(root, "MagPouch", new Vector3(x, 0.07f, front + 0.05f), new Vector3(0.07f, 0.12f, 0.05f), pouch);
                Box(root, "MagFlap", new Vector3(x, 0.135f, front + 0.05f), new Vector3(0.074f, 0.02f, 0.054f), rig);
            }
            if (large)
            {
                Box(root, "SidePouchL", new Vector3(-0.21f, 0.0f, 0.03f), new Vector3(0.05f, 0.11f, 0.09f), pouch);
                Box(root, "SidePouchR", new Vector3(0.21f, 0.0f, 0.03f), new Vector3(0.05f, 0.11f, 0.09f), pouch);
                Box(root, "Radio", new Vector3(-0.12f, 0.2f, front + 0.03f), new Vector3(0.05f, 0.1f, 0.04f), profile.Lit(profile.Pads));
                Cylinder(root, "Antenna", new Vector3(-0.13f, 0.32f, front + 0.02f), 0.005f, 0.16f, Quaternion.identity, profile.Lit(profile.Pads));
            }
            return root;
        }

        /// <summary>Backpack on the back of whatever is worn underneath. Returns how far it sticks out (for slung weapons).</summary>
        public static Transform Backpack(Transform chest, GearShape shape, GearShape armor, Color color, VisualProfile profile, out float depth)
        {
            Transform root = Node(chest, "Backpack", Vector3.zero);
            Material bag = profile.Lit(color);
            Material dark = profile.Lit(profile.Pads);
            float back = BackOf(armor);
            float front = FrontOf(armor);
            bool large = shape == GearShape.BackpackLarge;
            depth = large ? 0.22f : 0.15f;
            float width = large ? 0.36f : 0.3f;
            float height = large ? 0.5f : 0.34f;
            float centerY = large ? 0.07f : 0.12f;
            float z = back - depth * 0.5f;

            Box(root, "Bag", new Vector3(0f, centerY, z), new Vector3(width, height, depth), bag);
            Box(root, "Flap", new Vector3(0f, centerY + height * 0.5f, z - 0.005f), new Vector3(width + 0.01f, 0.05f, depth + 0.01f), bag);
            Box(root, "FrontPocket", new Vector3(0f, centerY - height * 0.18f, z - depth * 0.5f - 0.025f), new Vector3(width * 0.7f, height * 0.4f, 0.05f), dark);
            Box(root, "StrapL", new Vector3(-0.1f, 0.24f, (front + back) * 0.5f), new Vector3(0.05f, 0.02f, front - back + 0.03f), dark);
            Box(root, "StrapR", new Vector3(0.1f, 0.24f, (front + back) * 0.5f), new Vector3(0.05f, 0.02f, front - back + 0.03f), dark);
            if (large)
            {
                Box(root, "SidePocketL", new Vector3(-width * 0.5f - 0.03f, centerY - 0.05f, z), new Vector3(0.06f, 0.24f, depth * 0.7f), bag);
                Box(root, "SidePocketR", new Vector3(width * 0.5f + 0.03f, centerY - 0.05f, z), new Vector3(0.06f, 0.24f, depth * 0.7f), bag);
                Cylinder(root, "Bedroll", new Vector3(0f, centerY + height * 0.5f + 0.07f, z), 0.06f, width + 0.04f, AlongX, dark);
                depth += 0.06f;
            }
            else
            {
                depth += 0.05f;
            }
            return root;
        }

        /// <summary>Sheathed knife on the left of the belt (hips bone).</summary>
        public static Transform Knife(Transform hips, VisualProfile profile)
        {
            Transform root = Node(hips, "Knife", new Vector3(-0.175f, 0.04f, 0.04f), Quaternion.Euler(-12f, 0f, 0f));
            Box(root, "Sheath", new Vector3(0f, -0.06f, 0f), new Vector3(0.025f, 0.15f, 0.045f), profile.Lit(profile.Pads));
            Cylinder(root, "Handle", new Vector3(0f, 0.06f, 0f), 0.014f, 0.09f, Quaternion.identity, profile.Lit(profile.Polymer));
            Box(root, "Guard", new Vector3(0f, 0.015f, 0f), new Vector3(0.03f, 0.008f, 0.05f), profile.Lit(profile.GunMetal));
            return root;
        }
    }
}
