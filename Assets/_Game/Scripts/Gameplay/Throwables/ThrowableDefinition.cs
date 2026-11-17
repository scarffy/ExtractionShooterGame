using ExtractionShooter.Core.Throwables;
using ExtractionShooter.Gameplay.Inventory;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Throwables
{
    /// <summary>
    /// A hand grenade (frag, smoke or flash). Carried loose in the rig, pockets or backpack and thrown with G
    /// (the type picked with B); one item is used up per throw. The fields under each header only matter for that kind.
    /// </summary>
    [CreateAssetMenu(fileName = "Throwable", menuName = "Extraction Shooter/Items/Throwable")]
    public sealed class ThrowableDefinition : ItemDefinition
    {
        [Header("Throw")]
        [SerializeField] private ThrowableKind _kind;

        [Tooltip("Seconds from the throw until it goes off.")]
        [SerializeField, Min(0.1f)] private float _fuseSeconds = 3.5f;

        [Tooltip("Throw speed along the aim direction (m/s).")]
        [SerializeField, Min(1f)] private float _throwSpeed = 14f;

        [Tooltip("How far bots hear it go off (metres).")]
        [SerializeField, Min(0f)] private float _noiseRange = 120f;

        [Header("Frag and flash")]
        [Tooltip("Full effect within this distance (metres).")]
        [SerializeField, Min(0f)] private float _innerRadius = 2.5f;

        [Tooltip("No effect beyond this distance (metres). Smoke: the cloud's radius.")]
        [SerializeField, Min(0.5f)] private float _radius = 9f;

        [Header("Frag")]
        [Tooltip("Damage at point blank, falling off linearly to nothing at the radius.")]
        [SerializeField, Min(0)] private int _damage = 140;

        [Tooltip("Share of the damage that hits the legs (no armor there); the rest hits the body (armor applies).")]
        [SerializeField, Range(0f, 1f)] private float _legShare = 0.35f;

        [Tooltip("Penetration of the fragments against armor class x 10.")]
        [SerializeField, Min(0f)] private float _penetration = 25f;

        [Tooltip("Armor damage, percent of the hit.")]
        [SerializeField, Min(0f)] private float _armorDamagePercent = 40f;

        [Header("Flash")]
        [Tooltip("White-out seconds for a glance at the edge of the radius.")]
        [SerializeField, Min(0f)] private float _flashMinSeconds = 1.5f;

        [Tooltip("White-out seconds when staring at it point blank.")]
        [SerializeField, Min(0f)] private float _flashMaxSeconds = 4f;

        [Tooltip("Looking within this angle of it (degrees) takes the full flash.")]
        [SerializeField, Range(0f, 180f)] private float _flashFullAngle = 45f;

        [Tooltip("Looking this far away from it (degrees) or more is not affected.")]
        [SerializeField, Range(0f, 180f)] private float _flashZeroAngle = 115f;

        [Header("Smoke")]
        [Tooltip("Seconds the cloud lasts.")]
        [SerializeField, Min(1f)] private float _smokeSeconds = 20f;

        [Tooltip("Seconds for the cloud to reach full size.")]
        [SerializeField, Min(0f)] private float _smokeGrowSeconds = 2.5f;

        [Tooltip("Seconds over which the cloud thins out at the end.")]
        [SerializeField, Min(0f)] private float _smokeFadeSeconds = 3f;

        public ThrowableKind Kind => _kind;
        public float FuseSeconds => _fuseSeconds;
        public float ThrowSpeed => _throwSpeed;
        public float NoiseRange => _noiseRange;
        public float InnerRadius => _innerRadius;
        public float Radius => _radius;
        public int Damage => _damage;
        public float LegShare => _legShare;
        public float Penetration => _penetration;
        public float ArmorDamagePercent => _armorDamagePercent;
        public float FlashMinSeconds => _flashMinSeconds;
        public float FlashMaxSeconds => _flashMaxSeconds;
        public float FlashFullAngle => _flashFullAngle;
        public float FlashZeroAngle => _flashZeroAngle;
        public float SmokeSeconds => _smokeSeconds;
        public float SmokeGrowSeconds => _smokeGrowSeconds;
        public float SmokeFadeSeconds => _smokeFadeSeconds;

        /// <summary>Flash strength 0..1 for a viewer at <paramref name="distance"/> looking <paramref name="angleDegrees"/> away from it.</summary>
        public float FlashIntensity(float distance, float angleDegrees) =>
            ThrowableMath.FlashIntensity(distance, _innerRadius, _radius, angleDegrees, _flashFullAngle, _flashZeroAngle);

        /// <summary>White-out seconds for a flash of <paramref name="intensity"/> (0 when too weak to matter).</summary>
        public float FlashSeconds(float intensity) => ThrowableMath.FlashSeconds(intensity, _flashMinSeconds, _flashMaxSeconds);

        /// <summary>Short HUD name for a kind.</summary>
        public static string ShortName(ThrowableKind kind)
        {
            switch (kind)
            {
                case ThrowableKind.Smoke: return "SMOKE";
                case ThrowableKind.Flash: return "FLASH";
                default: return "FRAG";
            }
        }
    }
}
