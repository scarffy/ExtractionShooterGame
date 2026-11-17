using ExtractionShooter.Core.Combat;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Combat
{
    /// <summary>
    /// A trigger collider marking a body zone (head, body) for hit detection.
    /// Weapons read the zone for armor and the multiplier for damage (e.g. headshots).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class Hitbox : MonoBehaviour
    {
        [SerializeField] private HitZone _zone = HitZone.Body;
        [SerializeField, Min(0f)] private float _damageMultiplier = 1f;

        public HitZone Zone => _zone;
        public float DamageMultiplier => _damageMultiplier;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }
    }
}
