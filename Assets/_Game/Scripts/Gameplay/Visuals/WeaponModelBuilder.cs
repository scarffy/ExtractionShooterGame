using System.Collections.Generic;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Weapons;
using UnityEngine;
using static ExtractionShooter.Gameplay.Visuals.PrimitiveParts;

namespace ExtractionShooter.Gameplay.Visuals
{
    /// <summary>
    /// Builds a primitive weapon for a <see cref="WeaponDefinition"/> and its fitted attachments: the shape follows the
    /// weapon (<see cref="VisualClassifier"/>: pistol, SMG, rifle, shotgun, marksman, sniper), and each attachment
    /// shows (optic with a glowing reticle on the line of sight, suppressor or compensator, grip, extended magazine).
    /// Used for the weapon in a character's hands, slung weapons and the first-person view.
    /// </summary>
    public static class WeaponModelBuilder
    {
        // Line-of-sight heights above the grip origin, per base shape.
        private const float RifleSightY = 0.105f;
        private const float PistolSightY = 0.064f;

        private sealed class Kit
        {
            public VisualProfile Profile;
            public Transform Root;
            public Material Metal, Polymer, Furniture, Glass, Glow, Flash;
        }

        /// <summary>
        /// Builds the weapon under <paramref name="parent"/> (grip at the parent's origin, barrel along +Z).
        /// <paramref name="mods"/> may be null or contain nulls.
        /// </summary>
        public static WeaponModel Build(Transform parent, WeaponDefinition weapon, IReadOnlyList<AttachmentDefinition> mods,
            VisualProfile profile)
        {
            if (weapon == null || profile == null) return null;

            GearShape shape = VisualClassifier.Of(weapon, profile);
            Transform root = Node(parent, weapon.Id + " (model)", Vector3.zero);
            var kit = new Kit
            {
                Profile = profile,
                Root = root,
                Metal = profile.Lit(profile.GunMetal),
                Polymer = profile.Lit(profile.Polymer),
                Furniture = profile.Lit(profile.TintOr(weapon.Id, profile.Polymer)),
                Glass = profile.Lit(profile.OpticGlass),
                Glow = profile.Glow(profile.Reticle),
                Flash = profile.Glow(profile.MuzzleFlash),
            };

            AttachmentDefinition optic = null, muzzle = null, grip = null, magazine = null;
            if (mods != null)
            {
                foreach (AttachmentDefinition mod in mods)
                {
                    if (mod == null) continue;
                    switch (mod.Slot)
                    {
                        case AttachmentSlot.Optic: optic = mod; break;
                        case AttachmentSlot.Muzzle: muzzle = mod; break;
                        case AttachmentSlot.Grip: grip = mod; break;
                        case AttachmentSlot.Magazine: magazine = mod; break;
                    }
                }
            }

            bool extendedMag = magazine != null;
            Frame frame;
            switch (shape)
            {
                case GearShape.Pistol: frame = BuildPistol(kit, weapon, extendedMag); break;
                case GearShape.Smg: frame = BuildSmg(kit, weapon, extendedMag); break;
                case GearShape.Shotgun: frame = BuildShotgun(kit, extendedMag); break;
                case GearShape.Marksman: frame = BuildLongRifle(kit, weapon, extendedMag, false); break;
                case GearShape.Sniper: frame = BuildLongRifle(kit, weapon, extendedMag, true); break;
                default: frame = BuildRifle(kit, weapon, extendedMag); break;
            }

            // Optic: replaces the iron sights / integral scope line of sight.
            if (optic != null) frame = FitOptic(kit, frame, VisualClassifier.Of(optic, profile));
            else if (frame.IntegralScope) frame = AddScope(kit, frame, 0.022f, 0.3f, frame.RailZ);
            else if (frame.IronFrontZ > frame.IronRearZ) IronSights(kit, frame.IronRearZ, frame.IronFrontZ, frame.Sight.y);

            if (muzzle != null) frame = FitMuzzle(kit, frame, VisualClassifier.Of(muzzle, profile), shape == GearShape.Pistol);
            if (grip != null && shape != GearShape.Pistol) frame = FitGrip(kit, frame, VisualClassifier.Of(grip, profile));

            Transform gripSocket = Node(root, "Grip", frame.Grip);
            Transform supportSocket = Node(root, "Support", frame.Support);
            Transform sightSocket = Node(root, "Sight", frame.Sight);
            Transform muzzleSocket = Node(root, "Muzzle", frame.Muzzle);
            Transform flash = BuildFlash(kit, muzzleSocket, shape == GearShape.Pistol ? 0.75f : 1f);

            WeaponModel model = root.gameObject.AddComponent<WeaponModel>();
            model.Configure(shape, gripSocket, supportSocket, sightSocket, muzzleSocket, frame.EyeRelief, frame.Scoped,
                new Vector3(0f, 0.03f, (frame.Muzzle.z + frame.StockEnd) * 0.5f), flash);
            return model;
        }

        /// <summary>Key points of a weapon while it is being built.</summary>
        private struct Frame
        {
            public Vector3 Grip, Support, Sight, Muzzle;
            public float EyeRelief;
            public bool Scoped;
            public bool IntegralScope;
            public bool Pistol;

            /// <summary>Iron sight positions (built only when no optic is fitted). Front not past rear: none.</summary>
            public float IronRearZ, IronFrontZ;

            /// <summary>Optic mount: top of the receiver rail (y) at this z.</summary>
            public float RailY, RailZ;

            /// <summary>Lowest point of the handguard (grips hang from it) and the rear end of the stock.</summary>
            public float HandguardBottom, StockEnd;
        }

        // ---------------- Base shapes ----------------

        private static Frame BuildRifle(Kit kit, WeaponDefinition weapon, bool extendedMag)
        {
            Transform r = kit.Root;
            bool battle = weapon.Caliber == Caliber.Mm762x51;
            bool curvedMag = weapon.Caliber == Caliber.Mm762x39 || weapon.Caliber == Caliber.Mm545x39;
            float barrel = battle ? 0.22f : 0.18f;

            PistolGrip(kit);
            Box(r, "Receiver", new Vector3(0f, 0.035f, 0.07f), new Vector3(0.055f, 0.085f, 0.4f), kit.Metal);
            Box(r, "Rail", new Vector3(0f, 0.083f, 0.08f), new Vector3(0.03f, 0.012f, 0.3f), kit.Metal);
            Box(r, "Handguard", new Vector3(0f, 0.04f, 0.38f), new Vector3(0.062f, 0.066f, 0.24f), kit.Furniture);
            Cylinder(r, "Barrel", new Vector3(0f, 0.045f, 0.5f + barrel * 0.5f), 0.011f, barrel, AlongZ, kit.Metal);
            Box(r, "Stock", new Vector3(0f, 0.01f, -0.25f), new Vector3(0.045f, 0.08f, 0.24f), kit.Furniture);
            Box(r, "Buttpad", new Vector3(0f, 0f, -0.375f), new Vector3(0.05f, 0.125f, 0.02f), kit.Polymer);
            Magazine(kit, new Vector3(0f, -0.085f, 0.14f), new Vector3(0.028f, 0.16f, 0.07f), curvedMag ? -20f : -8f, extendedMag);
            return new Frame
            {
                IronRearZ = -0.08f, IronFrontZ = 0.48f,
                Grip = new Vector3(0f, -0.04f, 0f), Support = new Vector3(-0.005f, 0.0f, 0.3f),
                Sight = new Vector3(0f, RifleSightY, -0.08f), Muzzle = new Vector3(0f, 0.045f, 0.5f + barrel),
                EyeRelief = 0.075f, RailY = 0.089f, RailZ = 0.07f, HandguardBottom = 0.007f, StockEnd = -0.385f,
            };
        }

        private static Frame BuildSmg(Kit kit, WeaponDefinition weapon, bool extendedMag)
        {
            Transform r = kit.Root;
            bool pdw = weapon.Caliber == Caliber.Mm46x30;

            PistolGrip(kit);
            Box(r, "Receiver", new Vector3(0f, 0.035f, 0.03f), new Vector3(0.05f, 0.08f, 0.26f), kit.Metal);
            Box(r, "Rail", new Vector3(0f, 0.08f, 0.04f), new Vector3(0.028f, 0.01f, 0.2f), kit.Metal);
            Box(r, "Handguard", new Vector3(0f, 0.035f, 0.23f), new Vector3(0.056f, 0.06f, 0.14f), kit.Furniture);
            Cylinder(r, "Barrel", new Vector3(0f, 0.045f, 0.33f), 0.01f, 0.06f, AlongZ, kit.Metal);
            Box(r, "StockRodL", new Vector3(-0.018f, 0.03f, -0.2f), new Vector3(0.008f, 0.008f, 0.2f), kit.Metal);
            Box(r, "StockRodR", new Vector3(0.018f, 0.03f, -0.2f), new Vector3(0.008f, 0.008f, 0.2f), kit.Metal);
            Box(r, "Buttplate", new Vector3(0f, 0.015f, -0.3f), new Vector3(0.05f, 0.1f, 0.016f), kit.Polymer);
            if (pdw)
                // The PDW's magazine sits in the grip.
                Magazine(kit, new Vector3(0f, -0.13f, -0.012f), new Vector3(0.024f, 0.06f, 0.035f), 18f, extendedMag);
            else
                Magazine(kit, new Vector3(0f, -0.08f, 0.1f), new Vector3(0.024f, 0.15f, 0.045f), weapon.Caliber == Caliber.Mm9x19 ? -12f : -4f, extendedMag);
            return new Frame
            {
                IronRearZ = -0.07f, IronFrontZ = 0.28f,
                Grip = new Vector3(0f, -0.04f, 0f), Support = new Vector3(-0.005f, -0.005f, 0.23f),
                Sight = new Vector3(0f, 0.1f, -0.07f), Muzzle = new Vector3(0f, 0.045f, 0.36f),
                EyeRelief = 0.08f, RailY = 0.085f, RailZ = 0.03f, HandguardBottom = 0.005f, StockEnd = -0.31f,
            };
        }

        private static Frame BuildShotgun(Kit kit, bool extendedMag)
        {
            Transform r = kit.Root;
            PistolGrip(kit);
            Box(r, "Receiver", new Vector3(0f, 0.035f, 0.03f), new Vector3(0.05f, 0.08f, 0.2f), kit.Metal);
            Cylinder(r, "Barrel", new Vector3(0f, 0.055f, 0.38f), 0.013f, 0.5f, AlongZ, kit.Metal);
            float tube = extendedMag ? 0.48f : 0.36f;
            Cylinder(r, "MagTube", new Vector3(0f, 0.02f, 0.13f + tube * 0.5f), 0.013f, tube, AlongZ, kit.Metal);
            Box(r, "Pump", new Vector3(0f, 0.02f, 0.33f), new Vector3(0.056f, 0.05f, 0.16f), kit.Furniture);
            Box(r, "Stock", new Vector3(0f, -0.005f, -0.22f), new Vector3(0.045f, 0.09f, 0.3f), Quaternion.Euler(-4f, 0f, 0f), kit.Furniture);
            Box(r, "Buttpad", new Vector3(0f, -0.015f, -0.375f), new Vector3(0.05f, 0.12f, 0.02f), kit.Polymer);
            Sphere(r, "Bead", new Vector3(0f, 0.072f, 0.61f), new Vector3(0.007f, 0.007f, 0.007f), kit.Metal);
            Box(r, "Rail", new Vector3(0f, 0.08f, 0.03f), new Vector3(0.026f, 0.01f, 0.16f), kit.Metal);

            return new Frame
            {
                Grip = new Vector3(0f, -0.04f, 0f), Support = new Vector3(-0.005f, -0.01f, 0.29f),
                Sight = new Vector3(0f, 0.075f, 0f), Muzzle = new Vector3(0f, 0.055f, 0.63f),
                EyeRelief = 0.12f, RailY = 0.085f, RailZ = 0.03f, HandguardBottom = -0.005f, StockEnd = -0.385f,
            };
        }

        private static Frame BuildLongRifle(Kit kit, WeaponDefinition weapon, bool extendedMag, bool boltAction)
        {
            Transform r = kit.Root;
            float barrel = boltAction ? 0.42f : 0.3f;
            float handguardEnd = boltAction ? 0.48f : 0.52f;

            PistolGrip(kit);
            Box(r, "Receiver", new Vector3(0f, 0.035f, 0.07f), new Vector3(0.056f, 0.085f, 0.4f), kit.Metal);
            Box(r, "Rail", new Vector3(0f, 0.083f, 0.08f), new Vector3(0.03f, 0.012f, 0.32f), kit.Metal);
            Box(r, "Handguard", new Vector3(0f, 0.04f, (0.26f + handguardEnd) * 0.5f), new Vector3(0.064f, 0.068f, handguardEnd - 0.26f), kit.Furniture);
            Cylinder(r, "Barrel", new Vector3(0f, 0.045f, handguardEnd + barrel * 0.5f), boltAction ? 0.013f : 0.012f, barrel, AlongZ, kit.Metal);
            if (boltAction)
            {
                Box(r, "Stock", new Vector3(0f, -0.005f, -0.26f), new Vector3(0.05f, 0.12f, 0.27f), kit.Furniture);
                Box(r, "Cheek", new Vector3(0f, 0.065f, -0.24f), new Vector3(0.045f, 0.03f, 0.16f), kit.Polymer);
                Cylinder(r, "Bolt", new Vector3(0.045f, 0.05f, 0.0f), 0.006f, 0.05f, AlongX, kit.Metal);
                Sphere(r, "BoltKnob", new Vector3(0.072f, 0.045f, 0.0f), new Vector3(0.018f, 0.018f, 0.018f), kit.Metal);
                Magazine(kit, new Vector3(0f, -0.04f, 0.13f), new Vector3(0.03f, 0.07f, 0.08f), 0f, extendedMag);
            }
            else
            {
                Box(r, "Stock", new Vector3(0f, 0.01f, -0.25f), new Vector3(0.046f, 0.09f, 0.25f), kit.Furniture);
                Magazine(kit, new Vector3(0f, -0.07f, 0.14f), new Vector3(0.03f, 0.12f, 0.08f), weapon.Caliber == Caliber.Mm762x54R ? -10f : -3f, extendedMag);
            }
            Box(r, "Buttpad", new Vector3(0f, 0f, boltAction ? -0.4f : -0.385f), new Vector3(0.052f, 0.13f, 0.02f), kit.Polymer);

            return new Frame
            {
                Grip = new Vector3(0f, -0.04f, 0f), Support = new Vector3(-0.005f, 0.0f, 0.31f),
                Sight = new Vector3(0f, RifleSightY, -0.08f), Muzzle = new Vector3(0f, 0.045f, handguardEnd + barrel),
                EyeRelief = 0.075f, IntegralScope = true, RailY = 0.089f, RailZ = 0.07f, HandguardBottom = 0.006f,
                StockEnd = boltAction ? -0.41f : -0.395f,
            };
        }

        private static Frame BuildPistol(Kit kit, WeaponDefinition weapon, bool extendedMag)
        {
            Transform r = kit.Root;
            float s = weapon.Caliber == Caliber.Ae50 ? 1.15f : 1f;
            Box(r, "GripFrame", new Vector3(0f, -0.035f, -0.005f) * s, new Vector3(0.028f, 0.1f, 0.045f) * s, Quaternion.Euler(15f, 0f, 0f), kit.Polymer);
            Box(r, "Frame", new Vector3(0f, 0.012f, 0.045f) * s, new Vector3(0.026f, 0.025f, 0.14f) * s, kit.Polymer);
            Box(r, "Slide", new Vector3(0f, 0.04f, 0.035f) * s, new Vector3(0.028f, 0.032f, 0.19f) * s, kit.Metal);
            Box(r, "TriggerGuard", new Vector3(0f, -0.007f, 0.035f) * s, new Vector3(0.006f, 0.02f, 0.045f) * s, kit.Polymer);
            Box(r, "RearSight", new Vector3(0f, 0.06f, -0.05f) * s, new Vector3(0.02f, 0.008f, 0.01f) * s, kit.Metal);
            Box(r, "FrontSight", new Vector3(0f, 0.06f, 0.12f) * s, new Vector3(0.004f, 0.008f, 0.006f) * s, kit.Metal);
            if (extendedMag)
                Box(r, "MagExtension", new Vector3(0f, -0.095f, -0.022f) * s, new Vector3(0.024f, 0.05f, 0.036f) * s, Quaternion.Euler(15f, 0f, 0f), kit.Metal);

            return new Frame
            {
                Pistol = true,
                Grip = new Vector3(0f, -0.03f, -0.005f) * s, Support = new Vector3(-0.022f, -0.04f, 0.005f) * s,
                Sight = new Vector3(0f, PistolSightY, -0.05f) * s, Muzzle = new Vector3(0f, 0.04f, 0.13f) * s,
                EyeRelief = 0.3f, RailY = 0.056f * s, RailZ = 0.0f, HandguardBottom = -0.01f, StockEnd = -0.06f * s,
            };
        }

        // ---------------- Shared parts ----------------

        private static void PistolGrip(Kit kit) =>
            Box(kit.Root, "PistolGrip", new Vector3(0f, -0.045f, -0.005f), new Vector3(0.028f, 0.1f, 0.045f), Quaternion.Euler(18f, 0f, 0f), kit.Polymer);

        private static void Magazine(Kit kit, Vector3 center, Vector3 size, float tilt, bool extended)
        {
            if (extended)
            {
                float extra = size.y * 0.5f;
                Quaternion rotation = Quaternion.Euler(tilt, 0f, 0f);
                center += rotation * new Vector3(0f, -extra * 0.5f, 0f);
                size.y += extra;
            }
            Box(kit.Root, "Magazine", center, size, Quaternion.Euler(tilt, 0f, 0f), kit.Metal);
        }

        private static void IronSights(Kit kit, float rearZ, float frontZ, float sightY)
        {
            Transform r = kit.Root;
            // Rear: two ears with the line of sight between them. Front: a post whose tip is on the line of sight.
            Box(r, "RearSightBase", new Vector3(0f, sightY - 0.018f, rearZ), new Vector3(0.026f, 0.012f, 0.016f), kit.Metal);
            Box(r, "RearSightL", new Vector3(-0.009f, sightY - 0.004f, rearZ), new Vector3(0.006f, 0.022f, 0.012f), kit.Metal);
            Box(r, "RearSightR", new Vector3(0.009f, sightY - 0.004f, rearZ), new Vector3(0.006f, 0.022f, 0.012f), kit.Metal);
            Box(r, "FrontSightBase", new Vector3(0f, sightY - 0.03f, frontZ), new Vector3(0.024f, 0.02f, 0.02f), kit.Metal);
            Box(r, "FrontSightPost", new Vector3(0f, sightY - 0.011f, frontZ), new Vector3(0.004f, 0.022f, 0.005f), kit.Metal);
            Box(r, "FrontSightEarL", new Vector3(-0.011f, sightY - 0.006f, frontZ), new Vector3(0.004f, 0.03f, 0.006f), kit.Metal);
            Box(r, "FrontSightEarR", new Vector3(0.011f, sightY - 0.006f, frontZ), new Vector3(0.004f, 0.03f, 0.006f), kit.Metal);
        }

        // ---------------- Attachments ----------------

        private static Frame FitOptic(Kit kit, Frame frame, GearShape optic)
        {
            switch (optic)
            {
                case GearShape.Scope: return AddScope(kit, frame, 0.018f, 0.2f, frame.RailZ);
                case GearShape.Holo: return AddReflex(kit, frame, true);
                default: return AddReflex(kit, frame, false);
            }
        }

        /// <summary>
        /// Open reflex sight: a hood with a window and the reticle (dot, or ring and dot for a holographic sight) glowing
        /// on the line of sight at the front of the window, so aiming puts it at the centre of the screen.
        /// </summary>
        private static Frame AddReflex(Kit kit, Frame frame, bool holo)
        {
            Transform r = kit.Root;
            bool pistol = frame.Pistol;
            float width = holo ? 0.046f : pistol ? 0.026f : 0.034f;
            float length = holo ? 0.075f : pistol ? 0.035f : 0.045f;
            float window = holo ? 0.034f : pistol ? 0.02f : 0.026f;
            float baseHeight = pistol ? 0.006f : 0.012f;
            float z = frame.RailZ;
            float baseY = frame.RailY + baseHeight * 0.5f;
            float sightY = frame.RailY + baseHeight + window * 0.5f;
            float postWidth = (width - window) * 0.5f;

            Box(r, "OpticBase", new Vector3(0f, baseY, z), new Vector3(width, baseHeight, length), kit.Metal);
            Box(r, "OpticSideL", new Vector3(-(window + postWidth) * 0.5f, sightY, z), new Vector3(postWidth, window, length), kit.Polymer);
            Box(r, "OpticSideR", new Vector3((window + postWidth) * 0.5f, sightY, z), new Vector3(postWidth, window, length), kit.Polymer);
            Box(r, "OpticTop", new Vector3(0f, sightY + (window + 0.006f) * 0.5f, z), new Vector3(width, 0.006f, length), kit.Polymer);

            float reticleZ = z + length * 0.5f - 0.002f;
            float dot = pistol ? 0.0014f : 0.0012f;
            Sphere(r, "ReticleDot", new Vector3(0f, sightY, reticleZ), new Vector3(dot, dot, dot), kit.Glow);
            if (holo)
            {
                const int segments = 12;
                const float ringRadius = 0.0055f;
                for (int i = 0; i < segments; i++)
                {
                    float angle = i * Mathf.PI * 2f / segments;
                    var offset = new Vector3(Mathf.Cos(angle) * ringRadius, Mathf.Sin(angle) * ringRadius, 0f);
                    Box(r, "ReticleRing", new Vector3(0f, sightY, reticleZ) + offset, new Vector3(0.0012f, 0.0012f, 0.0005f),
                        Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg), kit.Glow);
                }
            }

            frame.Sight = new Vector3(0f, sightY, z - length * 0.5f);
            frame.EyeRelief = pistol ? 0.28f : 0.13f;
            frame.Scoped = false;
            frame.IntegralScope = false;
            return frame;
        }

        /// <summary>A magnified scope: tube, bells, rings and lenses. The HUD's scope overlay shows when fully aimed.</summary>
        private static Frame AddScope(Kit kit, Frame frame, float radius, float length, float z)
        {
            Transform r = kit.Root;
            float centerY = frame.RailY + 0.012f + radius;
            Cylinder(r, "ScopeTube", new Vector3(0f, centerY, z), radius, length, AlongZ, kit.Metal);
            Cylinder(r, "ScopeObjective", new Vector3(0f, centerY, z + length * 0.5f), radius * 1.35f, 0.05f, AlongZ, kit.Metal);
            Cylinder(r, "ScopeEyepiece", new Vector3(0f, centerY, z - length * 0.5f), radius * 1.2f, 0.04f, AlongZ, kit.Metal);
            Cylinder(r, "ScopeTurret", new Vector3(0f, centerY + radius, z), radius * 0.55f, 0.022f, Quaternion.identity, kit.Metal);
            Box(r, "ScopeRingFront", new Vector3(0f, frame.RailY + 0.008f, z + length * 0.28f), new Vector3(0.024f, 0.016f, 0.014f), kit.Metal);
            Box(r, "ScopeRingRear", new Vector3(0f, frame.RailY + 0.008f, z - length * 0.28f), new Vector3(0.024f, 0.016f, 0.014f), kit.Metal);
            Cylinder(r, "ScopeLensFront", new Vector3(0f, centerY, z + length * 0.5f + 0.0255f), radius * 1.2f, 0.002f, AlongZ, kit.Glass);
            Cylinder(r, "ScopeLensRear", new Vector3(0f, centerY, z - length * 0.5f - 0.0205f), radius * 1.05f, 0.002f, AlongZ, kit.Glass);

            frame.Sight = new Vector3(0f, centerY, z - length * 0.5f - 0.02f);
            frame.EyeRelief = 0.07f;
            frame.Scoped = true;
            frame.IntegralScope = false;
            return frame;
        }

        private static Frame FitMuzzle(Kit kit, Frame frame, GearShape device, bool pistol)
        {
            bool suppressor = device == GearShape.Suppressor;
            float length = suppressor ? (pistol ? 0.12f : 0.16f) : 0.05f;
            float radius = suppressor ? (pistol ? 0.014f : 0.018f) : 0.014f;
            Vector3 center = frame.Muzzle + new Vector3(0f, 0f, length * 0.5f);
            Cylinder(kit.Root, suppressor ? "Suppressor" : "Compensator", center, radius, length, AlongZ, kit.Metal);
            if (!suppressor)
                Box(kit.Root, "CompensatorPort", center + new Vector3(0f, radius * 0.6f, 0f), new Vector3(radius * 1.2f, 0.004f, length * 0.6f), kit.Polymer);
            frame.Muzzle += new Vector3(0f, 0f, length);
            return frame;
        }

        private static Frame FitGrip(Kit kit, Frame frame, GearShape grip)
        {
            Vector3 mount = new Vector3(0f, frame.HandguardBottom, frame.Support.z + 0.01f);
            if (grip == GearShape.AngledGrip)
            {
                Box(kit.Root, "AngledGrip", mount + new Vector3(0f, -0.014f, 0f), new Vector3(0.024f, 0.03f, 0.075f), Quaternion.Euler(-24f, 0f, 0f), kit.Polymer);
                frame.Support = mount + new Vector3(-0.005f, -0.018f, 0.0f);
            }
            else
            {
                Cylinder(kit.Root, "VerticalGrip", mount + new Vector3(0f, -0.045f, 0f), 0.014f, 0.085f, Quaternion.identity, kit.Polymer);
                frame.Support = mount + new Vector3(-0.004f, -0.05f, 0f);
            }
            return frame;
        }

        // ---------------- Muzzle flash ----------------

        private static Transform BuildFlash(Kit kit, Transform muzzle, float size)
        {
            Transform flash = Node(muzzle, "Flash", Vector3.zero);
            Sphere(flash, "Core", new Vector3(0f, 0f, 0.05f * size), new Vector3(0.045f, 0.045f, 0.11f) * size, kit.Flash);
            for (int i = 0; i < 3; i++)
                Box(flash, "Petal", new Vector3(0f, 0f, 0.015f * size), new Vector3(0.12f * size, 0.008f, 0.004f),
                    Quaternion.Euler(0f, 0f, i * 60f), kit.Flash);
            return flash;
        }
    }
}
