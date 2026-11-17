using ExtractionShooter.Gameplay.Inventory;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Weapons
{
    /// <summary>Which equipment slots a weapon may occupy.</summary>
    public enum WeaponClass
    {
        /// <summary>Rifles, SMGs, shotguns, snipers: Primary 1 or Primary 2 only.</summary>
        Primary = 0,

        /// <summary>Handguns: Pistol slot only.</summary>
        Pistol = 1,
    }

    /// <summary>How the trigger behaves.</summary>
    public enum FireMode
    {
        /// <summary>One shot per trigger press (pistols, DMRs, bolt and pump actions).</summary>
        SemiAuto = 0,

        /// <summary>Fires while the trigger is held.</summary>
        FullAuto = 1,
    }

    /// <summary>
    /// A hitscan firearm. The weapon fixes caliber, magazine, fire rate, spread, range and reload time;
    /// the loaded <see cref="AmmoDefinition"/> fixes damage and penetration.
    /// Loaded ammo is stored per weapon instance on its <see cref="Core.Inventory.ItemStack"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "Weapon", menuName = "Extraction Shooter/Items/Weapon")]
    public sealed class WeaponDefinition : ItemDefinition
    {
        [Header("Weapon")]
        [SerializeField] private WeaponClass _weaponClass = WeaponClass.Primary;
        [SerializeField] private Caliber _caliber = Caliber.Mm556x45;
        [SerializeField] private FireMode _fireMode = FireMode.FullAuto;

        [Header("Magazine")]
        [SerializeField, Min(1)] private int _magazineSize = 30;
        [SerializeField, Min(0.1f)] private float _reloadSeconds = 2.4f;

        [Header("Ballistics")]
        [SerializeField, Min(1f)] private float _roundsPerMinute = 700f;
        [SerializeField, Min(1f)] private float _range = 300f;

        [Tooltip("Cone half-angle in degrees for each shot.")]
        [SerializeField, Range(0f, 15f)] private float _spread = 0.6f;

        [SerializeField] private LayerMask _hitMask = ~0;

        [Header("Server validation")]
        [Tooltip("Seconds of slack allowed on the fire-rate check for network jitter.")]
        [SerializeField, Min(0f)] private float _fireRateTolerance = 0.03f;

        [Tooltip("Max distance between the client's claimed shot origin and the server's eye position.")]
        [SerializeField, Min(0f)] private float _maxOriginError = 1.5f;

        public WeaponClass WeaponClass => _weaponClass;
        public Caliber Caliber => _caliber;
        public FireMode FireMode => _fireMode;
        public int MagazineSize => _magazineSize;
        public float ReloadSeconds => _reloadSeconds;
        public float RoundsPerMinute => _roundsPerMinute;
        public float SecondsBetweenShots => 60f / _roundsPerMinute;
        public float Range => _range;
        public float Spread => _spread;
        public LayerMask HitMask => _hitMask;
        public float FireRateTolerance => _fireRateTolerance;
        public float MaxOriginError => _maxOriginError;

        /// <summary>Weapons never stack.</summary>
        public override int MaxStack => 1;

        public bool Accepts(AmmoDefinition ammo) => ammo != null && ammo.Caliber == _caliber;
    }
}
