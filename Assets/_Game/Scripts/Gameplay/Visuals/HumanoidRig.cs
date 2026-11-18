using UnityEngine;
using static ExtractionShooter.Gameplay.Visuals.PrimitiveParts;

namespace ExtractionShooter.Gameplay.Visuals
{
    /// <summary>
    /// A 1.8 m humanoid made of primitives: bones (empty transforms) with the body's meshes under them, in the
    /// profile's uniform colours. Rest pose: standing, arms hanging, facing +Z, feet at the root.
    /// Limb bones hang along their local -Y, so <see cref="LimbChain"/> can pose them; the head centre sits at
    /// <see cref="HeadHeight"/>, matching the head hitbox. Posed every frame by <see cref="CharacterBody"/>.
    /// </summary>
    public sealed class HumanoidRig
    {
        public const float HipHeight = 0.93f;
        public const float HipWidth = 0.1f;
        public const float ThighLength = 0.44f;
        public const float ShinLength = 0.42f;
        public const float AnkleHeight = HipHeight - ThighLength - ShinLength;
        public const float SpineOffset = 0.07f;
        public const float ChestOffset = 0.17f;
        public const float NeckOffset = 0.3f;
        public const float HeadOffset = 0.08f;
        public const float HeadCenter = 0.1f;
        public const float ShoulderWidth = 0.2f;
        public const float ShoulderOffset = 0.25f;
        public const float UpperArmLength = 0.31f;
        public const float ForearmLength = 0.27f;

        /// <summary>Head centre above the feet when standing (matches the head hitbox).</summary>
        public const float HeadHeight = HipHeight + SpineOffset + ChestOffset + NeckOffset + HeadOffset + HeadCenter;

        /// <summary>Head centre above the hip joints when upright.</summary>
        public const float HeadAboveHip = HeadHeight - HipHeight;

        /// <summary>Chest-local Z of the chest's front and back faces (gear stacks outward from there).</summary>
        public const float ChestFront = 0.115f;
        public const float ChestBack = -0.115f;

        private HumanoidRig() { }

        public Transform Root { get; private set; }
        public Transform Hips { get; private set; }
        public Transform Spine { get; private set; }
        public Transform Chest { get; private set; }
        public Transform Neck { get; private set; }
        public Transform Head { get; private set; }

        /// <summary>Index 0 left, 1 right.</summary>
        public LimbChain[] Arms { get; } = new LimbChain[2];

        public LimbChain[] Legs { get; } = new LimbChain[2];

        /// <summary>The glove under each hand bone (moved onto the grip while holding a weapon).</summary>
        public Transform[] Gloves { get; } = new Transform[2];

        /// <summary>Bare-head details (hair or cap): hidden under a helmet.</summary>
        public Transform HeadCover { get; private set; }

        /// <summary>Builds the rig under <paramref name="root"/>.</summary>
        public static HumanoidRig Build(Transform root, VisualProfile profile)
        {
            var rig = new HumanoidRig { Root = root };
            Material skin = profile.Lit(profile.Skin);
            Material top = profile.Lit(profile.Top);
            Material bottom = profile.Lit(profile.Bottom);
            Material boots = profile.Lit(profile.Boots);
            Material gloves = profile.Lit(profile.Gloves);
            Material pads = profile.Lit(profile.Pads);

            rig.Hips = Node(root, "Hips", new Vector3(0f, HipHeight, 0f));
            Box(rig.Hips, "Pelvis", new Vector3(0f, 0.02f, 0f), new Vector3(0.33f, 0.2f, 0.22f), bottom);
            Box(rig.Hips, "Belt", new Vector3(0f, 0.1f, 0f), new Vector3(0.34f, 0.05f, 0.225f), pads);

            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? -HipWidth : HipWidth;
                string name = side == 0 ? "L" : "R";
                Transform thigh = Node(rig.Hips, "Thigh" + name, new Vector3(x, 0f, 0f));
                Capsule(thigh, "ThighMesh", new Vector3(0f, -ThighLength * 0.5f, 0f), 0.078f, ThighLength + 0.06f, Quaternion.identity, bottom);
                Transform shin = Node(thigh, "Shin" + name, new Vector3(0f, -ThighLength, 0f));
                Capsule(shin, "ShinMesh", new Vector3(0f, -ShinLength * 0.5f, 0f), 0.06f, ShinLength + 0.04f, Quaternion.identity, bottom);
                Box(shin, "KneePad", new Vector3(0f, -0.03f, 0.055f), new Vector3(0.1f, 0.11f, 0.04f), pads);
                Transform foot = Node(shin, "Foot" + name, new Vector3(0f, -ShinLength, 0f));
                Box(foot, "Boot", new Vector3(0f, -0.02f, 0.045f), new Vector3(0.11f, 0.11f, 0.27f), boots);
                Box(foot, "BootCuff", new Vector3(0f, 0.05f, 0f), new Vector3(0.12f, 0.08f, 0.13f), boots);
                rig.Legs[side] = new LimbChain(thigh, shin, foot, ThighLength, ShinLength);
            }

            rig.Spine = Node(rig.Hips, "Spine", new Vector3(0f, SpineOffset, 0f));
            Box(rig.Spine, "Abdomen", new Vector3(0f, 0.08f, 0f), new Vector3(0.31f, 0.2f, 0.2f), top);

            rig.Chest = Node(rig.Spine, "Chest", new Vector3(0f, ChestOffset, 0f));
            Box(rig.Chest, "Torso", new Vector3(0f, 0.13f, 0f), new Vector3(0.38f, 0.3f, ChestFront - ChestBack), top);
            Sphere(rig.Chest, "ShoulderL", new Vector3(-ShoulderWidth, ShoulderOffset, 0f), new Vector3(0.14f, 0.13f, 0.15f), top);
            Sphere(rig.Chest, "ShoulderR", new Vector3(ShoulderWidth, ShoulderOffset, 0f), new Vector3(0.14f, 0.13f, 0.15f), top);

            rig.Neck = Node(rig.Chest, "Neck", new Vector3(0f, NeckOffset, 0f));
            Cylinder(rig.Neck, "NeckMesh", new Vector3(0f, 0.035f, 0f), 0.055f, 0.1f, Quaternion.identity, skin);
            rig.Head = Node(rig.Neck, "Head", new Vector3(0f, HeadOffset, 0f));
            Sphere(rig.Head, "HeadMesh", new Vector3(0f, HeadCenter, 0.01f), new Vector3(0.19f, 0.235f, 0.215f), skin);
            Box(rig.Head, "Eyewear", new Vector3(0f, HeadCenter + 0.02f, 0.1f), new Vector3(0.175f, 0.035f, 0.03f), profile.Lit(profile.Eyewear));
            if (profile.HasMask)
                Box(rig.Head, "Mask", new Vector3(0f, HeadCenter - 0.055f, 0.075f), new Vector3(0.18f, 0.085f, 0.075f), profile.Lit(profile.Mask));

            rig.HeadCover = Node(rig.Head, "HeadCover", Vector3.zero);
            if (profile.CapWhenBareheaded)
            {
                Sphere(rig.HeadCover, "Cap", new Vector3(0f, HeadCenter + 0.05f, 0f), new Vector3(0.205f, 0.175f, 0.228f), profile.Lit(profile.Cap));
                Box(rig.HeadCover, "CapFold", new Vector3(0f, HeadCenter + 0.025f, 0f), new Vector3(0.205f, 0.035f, 0.222f), profile.Lit(profile.Cap));
            }
            else
            {
                Sphere(rig.HeadCover, "Hair", new Vector3(0f, HeadCenter + 0.04f, -0.008f), new Vector3(0.198f, 0.18f, 0.215f), profile.Lit(profile.Hair));
            }

            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? -ShoulderWidth : ShoulderWidth;
                string name = side == 0 ? "L" : "R";
                Transform upper = Node(rig.Chest, "UpperArm" + name, new Vector3(x, ShoulderOffset, 0f));
                Capsule(upper, "UpperArmMesh", new Vector3(0f, -UpperArmLength * 0.5f, 0f), 0.056f, UpperArmLength + 0.05f, Quaternion.identity, top);
                Transform lower = Node(upper, "Forearm" + name, new Vector3(0f, -UpperArmLength, 0f));
                Capsule(lower, "ForearmMesh", new Vector3(0f, -ForearmLength * 0.5f, 0f), 0.048f, ForearmLength + 0.03f, Quaternion.identity, top);
                Transform hand = Node(lower, "Hand" + name, new Vector3(0f, -ForearmLength, 0f));
                rig.Gloves[side] = Node(hand, "Glove", new Vector3(0f, -0.045f, 0f));
                Box(rig.Gloves[side], "GloveMesh", Vector3.zero, new Vector3(0.045f, 0.09f, 0.075f), gloves);
                rig.Arms[side] = new LimbChain(upper, lower, hand, UpperArmLength, ForearmLength);
            }
            return rig;
        }

        /// <summary>
        /// Builds just a pair of arms (first-person view): shoulders at <paramref name="shoulders"/> (left, right) under
        /// <paramref name="root"/>, sleeves in the uniform colour, gloved hands.
        /// </summary>
        public static void BuildArms(Transform root, VisualProfile profile, Vector3 leftShoulder, Vector3 rightShoulder,
            float upperLength, float lowerLength, LimbChain[] arms, Transform[] gloves)
        {
            Material sleeve = profile.Lit(profile.Top);
            Material glove = profile.Lit(profile.Gloves);
            Material cuff = profile.Lit(profile.Pads);
            for (int side = 0; side < 2; side++)
            {
                string name = side == 0 ? "L" : "R";
                Transform upper = Node(root, "UpperArm" + name, side == 0 ? leftShoulder : rightShoulder);
                Capsule(upper, "UpperArmMesh", new Vector3(0f, -upperLength * 0.5f, 0f), 0.05f, upperLength + 0.05f, Quaternion.identity, sleeve);
                Transform lower = Node(upper, "Forearm" + name, new Vector3(0f, -upperLength, 0f));
                Capsule(lower, "ForearmMesh", new Vector3(0f, -lowerLength * 0.5f, 0f), 0.042f, lowerLength + 0.03f, Quaternion.identity, sleeve);
                Cylinder(lower, "Cuff", new Vector3(0f, -lowerLength + 0.03f, 0f), 0.045f, 0.035f, Quaternion.identity, cuff);
                Transform hand = Node(lower, "Hand" + name, new Vector3(0f, -lowerLength, 0f));
                gloves[side] = Node(hand, "Glove", Vector3.zero);
                Box(gloves[side], "Palm", Vector3.zero, new Vector3(0.042f, 0.085f, 0.07f), glove);
                Box(gloves[side], "Thumb", new Vector3(side == 0 ? 0.025f : -0.025f, 0.015f, 0.02f), new Vector3(0.018f, 0.045f, 0.022f), glove);
                arms[side] = new LimbChain(upper, lower, hand, upperLength, lowerLength);
            }
        }
    }
}
