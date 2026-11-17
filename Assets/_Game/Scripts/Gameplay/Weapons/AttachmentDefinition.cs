using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Inventory;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Weapons
{
    /// <summary>
    /// A weapon attachment (optic, muzzle device, grip, magazine). Fitted by dropping it on a weapon in the
    /// inventory; one per slot. Its modifiers apply through <see cref="WeaponStats"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "Attachment", menuName = "Extraction Shooter/Items/Attachment")]
    public sealed class AttachmentDefinition : ItemDefinition
    {
        [Header("Attachment")]
        [SerializeField] private AttachmentSlot _slot = AttachmentSlot.Optic;
        [SerializeField] private bool _fitsPrimary = true;
        [SerializeField] private bool _fitsPistol;

        [Header("Modifiers")]
        [Tooltip("Multiplies the weapon's base (aimed) spread.")]
        [SerializeField, Range(0.3f, 2f)] private float _spreadMultiplier = 1f;

        [Tooltip("Multiplies how much wider hip fire is.")]
        [SerializeField, Range(0.3f, 2f)] private float _hipSpreadMultiplier = 1f;

        [Tooltip("Multiplies the time to raise the sights.")]
        [SerializeField, Range(0.3f, 3f)] private float _aimSecondsMultiplier = 1f;

        [Tooltip("Optics: field of view when aiming. 0 keeps the weapon's own.")]
        [SerializeField, Range(0f, 90f)] private float _aimFieldOfView;

        [Tooltip("Optics: shows the scope overlay when fully aimed.")]
        [SerializeField] private bool _scoped;

        [Tooltip("Magazines: extra rounds.")]
        [SerializeField, Min(0)] private int _magazineBonus;

        [Tooltip("Muzzle devices: multiplies how far shots are heard (suppressor < 1).")]
        [SerializeField, Range(0.1f, 2f)] private float _soundRangeMultiplier = 1f;

        [Tooltip("Muzzle devices and grips: multiplies recoil kick (compensator < 1).")]
        [SerializeField, Range(0.3f, 2f)] private float _recoilMultiplier = 1f;

        public AttachmentSlot Slot => _slot;
        public float SpreadMultiplier => _spreadMultiplier;
        public float HipSpreadMultiplier => _hipSpreadMultiplier;
        public float AimSecondsMultiplier => _aimSecondsMultiplier;
        public float AimFieldOfView => _aimFieldOfView;
        public bool Scoped => _scoped;
        public int MagazineBonus => _magazineBonus;
        public float SoundRangeMultiplier => _soundRangeMultiplier;
        public float RecoilMultiplier => _recoilMultiplier;

        public bool Fits(WeaponDefinition weapon) =>
            weapon != null && (weapon.WeaponClass == WeaponClass.Pistol ? _fitsPistol : _fitsPrimary);

        public override int MaxStack => 1;
    }
}
