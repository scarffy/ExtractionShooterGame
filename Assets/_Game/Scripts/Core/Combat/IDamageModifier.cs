namespace ExtractionShooter.Core.Combat
{
    /// <summary>
    /// Server only: adjusts incoming damage before it is applied (armor, buffs).
    /// Implementations on the same object as the damageable are applied in component order.
    /// </summary>
    public interface IDamageModifier
    {
        DamageInfo ModifyIncomingDamage(in DamageInfo damage);
    }
}
