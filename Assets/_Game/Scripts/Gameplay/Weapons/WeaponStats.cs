using System.Collections.Generic;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Inventory;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Weapons
{
    /// <summary>
    /// A weapon's effective numbers with its fitted attachments: what aiming, spread, recoil, the magazine and the shot
    /// sound actually use. Computed from the definition and the item instance on the server and the owner alike.
    /// </summary>
    public readonly struct WeaponStats
    {
        public WeaponStats(WeaponDefinition weapon, float spread, float hipSpreadMultiplier, float aimSeconds, float aimFieldOfView,
            bool scoped, int magazineSize, float soundRangeMultiplier, float recoilMultiplier = 1f)
        {
            Weapon = weapon;
            Spread = spread;
            HipSpreadMultiplier = hipSpreadMultiplier;
            AimSeconds = aimSeconds;
            AimFieldOfView = aimFieldOfView;
            Scoped = scoped;
            MagazineSize = magazineSize;
            SoundRangeMultiplier = soundRangeMultiplier;
            RecoilMultiplier = recoilMultiplier;
        }

        public WeaponDefinition Weapon { get; }
        public float Spread { get; }
        public float HipSpreadMultiplier { get; }
        public float AimSeconds { get; }
        public float AimFieldOfView { get; }
        public bool Scoped { get; }
        public int MagazineSize { get; }
        public float SoundRangeMultiplier { get; }

        /// <summary>Product of the fitted attachments' recoil multipliers (1 = bare weapon).</summary>
        public float RecoilMultiplier { get; }

        public float MoveSpread => Weapon != null ? Weapon.MoveSpread : 0f;

        public static WeaponStats Of(WeaponDefinition weapon, in ItemStack item, ItemDatabase database)
        {
            if (weapon == null) return default;
            if (database == null || string.IsNullOrEmpty(item.Attachments)) return Of(weapon, (IEnumerable<AttachmentDefinition>)null);

            var mods = new List<AttachmentDefinition>(WeaponMods.SlotCount);
            foreach (var fitted in WeaponMods.All(item.Attachments))
                if (database.TryGet(fitted.Value, out ItemDefinition definition) && definition is AttachmentDefinition mod) mods.Add(mod);
            return Of(weapon, mods);
        }

        /// <summary>The weapon's numbers with the given attachments fitted.</summary>
        public static WeaponStats Of(WeaponDefinition weapon, IEnumerable<AttachmentDefinition> mods)
        {
            if (weapon == null) return default;

            float spread = weapon.Spread, hip = weapon.HipSpreadMultiplier, aimSeconds = weapon.AimSeconds;
            float fov = weapon.AimFieldOfView, sound = 1f, recoil = 1f;
            bool scoped = weapon.Scoped;
            int magazine = weapon.MagazineSize;

            if (mods != null)
            {
                foreach (AttachmentDefinition mod in mods)
                {
                    if (mod == null) continue;
                    spread *= mod.SpreadMultiplier;
                    hip *= mod.HipSpreadMultiplier;
                    aimSeconds *= mod.AimSecondsMultiplier;
                    if (mod.AimFieldOfView > 0f) { fov = mod.AimFieldOfView; scoped = mod.Scoped; }
                    magazine += mod.MagazineBonus;
                    sound *= mod.SoundRangeMultiplier;
                    recoil *= mod.RecoilMultiplier;
                }
            }
            return new WeaponStats(weapon, spread, Mathf.Max(1f, hip), aimSeconds, fov, scoped, Mathf.Max(1, magazine), sound, recoil);
        }
    }
}
