using UnityEngine;

namespace ExtractionShooter.Core.Combat
{
    /// <summary>
    /// Immutable description of a single damage event (one bullet or pellet).
    /// </summary>
    public readonly struct DamageInfo
    {
        /// <summary>Client id used when the damage has no player instigator (environment, server).</summary>
        public const int NoInstigator = -1;

        public DamageInfo(int amount, int instigatorClientId, Vector3 point, Vector3 direction,
            HitZone zone = HitZone.Body, float penetration = 0f, float armorDamagePercent = 0f)
        {
            Amount = amount;
            InstigatorClientId = instigatorClientId;
            Point = point;
            Direction = direction;
            Zone = zone;
            Penetration = penetration;
            ArmorDamagePercent = armorDamagePercent;
        }

        public int Amount { get; }
        public int InstigatorClientId { get; }
        public Vector3 Point { get; }
        public Vector3 Direction { get; }
        public HitZone Zone { get; }

        /// <summary>Penetration power of the round (same scale as armor class x 10).</summary>
        public float Penetration { get; }

        /// <summary>Armor durability lost per point of damage, in percent.</summary>
        public float ArmorDamagePercent { get; }

        public DamageInfo WithAmount(int amount) =>
            new DamageInfo(amount, InstigatorClientId, Point, Direction, Zone, Penetration, ArmorDamagePercent);
    }
}
