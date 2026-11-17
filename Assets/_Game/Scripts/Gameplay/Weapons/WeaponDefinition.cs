using ExtractionShooter.Core.Combat;
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
    /// A firearm. The weapon fixes caliber, magazine, fire rate, spread, range, muzzle velocity (bullet drop), recoil
    /// and reload time; the loaded <see cref="AmmoDefinition"/> fixes damage and penetration.
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

        [Tooltip("Muzzle velocity in m/s. Slower bullets drop more over distance (see Ballistics).")]
        [SerializeField, Range(50f, 1500f)] private float _muzzleVelocity = 800f;

        [Tooltip("Cone half-angle in degrees for each shot.")]
        [SerializeField, Range(0f, 15f)] private float _spread = 0.6f;

        [SerializeField] private LayerMask _hitMask = ~0;

        [Header("Aiming")]
        [Tooltip("Hip fire spread = spread x this. Aiming down sights uses the plain spread.")]
        [SerializeField, Min(1f)] private float _hipSpreadMultiplier = 3f;

        [Tooltip("Extra cone half-angle (degrees) at full movement speed; halved while aiming.")]
        [SerializeField, Range(0f, 10f)] private float _moveSpread = 2f;

        [Tooltip("Seconds to raise the sights.")]
        [SerializeField, Min(0f)] private float _aimSeconds = 0.25f;

        [Tooltip("Camera field of view while aiming (zoom).")]
        [SerializeField, Range(5f, 90f)] private float _aimFieldOfView = 50f;

        [Tooltip("Shows a scope overlay instead of iron-sight dots when fully aimed.")]
        [SerializeField] private bool _scoped;

        [Header("Recoil")]
        [Tooltip("Degrees the view climbs per shot at the start of a spray (eases to about half during long sprays).")]
        [SerializeField, Range(0f, 8f)] private float _recoilVertical = 0.6f;

        [Tooltip("Largest sideways kick per shot in degrees.")]
        [SerializeField, Range(0f, 4f)] private float _recoilHorizontal = 0.25f;

        [Tooltip("Sideways drift of the pattern: -1 left, 0 even sway, 1 right.")]
        [SerializeField, Range(-1f, 1f)] private float _recoilHorizontalBias;

        [Tooltip("Picks the shape of the sideways sway. Fixed per weapon so the spray pattern can be learned.")]
        [SerializeField] private int _recoilPatternSeed = 1;

        [Tooltip("Largest random wobble per shot in degrees, on top of the pattern.")]
        [SerializeField, Range(0f, 2f)] private float _recoilJitter = 0.08f;

        [Tooltip("How fast the view settles back after firing stops (per second, exponential).")]
        [SerializeField, Range(0.5f, 30f)] private float _recoilRecovery = 7f;

        [Tooltip("Seconds after the fire interval without shooting before the pattern starts over from the first shot.")]
        [SerializeField, Range(0f, 2f)] private float _recoilResetSeconds = 0.2f;

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
        public float MuzzleVelocity => _muzzleVelocity;
        public float Spread => _spread;
        public LayerMask HitMask => _hitMask;
        public float HipSpreadMultiplier => _hipSpreadMultiplier;
        public float MoveSpread => _moveSpread;
        public float AimSeconds => _aimSeconds;
        public float AimFieldOfView => _aimFieldOfView;
        public bool Scoped => _scoped;
        public RecoilProfile Recoil =>
            new RecoilProfile(_recoilVertical, _recoilHorizontal, _recoilHorizontalBias, _recoilPatternSeed, _recoilJitter);
        public float RecoilRecovery => _recoilRecovery;

        /// <summary>Pause after which the recoil pattern starts over (one shot interval plus the reset seconds).</summary>
        public double RecoilResetSeconds => RecoilPattern.ResetSeconds(SecondsBetweenShots, _recoilResetSeconds);

        public float FireRateTolerance => _fireRateTolerance;
        public float MaxOriginError => _maxOriginError;

        /// <summary>Weapons never stack.</summary>
        public override int MaxStack => 1;

        public bool Accepts(AmmoDefinition ammo) => ammo != null && ammo.Caliber == _caliber;

        /// <summary>Muzzle velocity (m/s) firing the given round: the weapon's, scaled by the round's multiplier.</summary>
        public float MuzzleVelocityWith(AmmoDefinition ammo) =>
            _muzzleVelocity * (ammo != null ? ammo.MuzzleVelocityMultiplier : 1f);
    }
}
