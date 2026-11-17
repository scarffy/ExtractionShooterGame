namespace ExtractionShooter.Core.Combat
{
    /// <summary>
    /// Anything that can receive damage. Implementations must only apply
    /// damage on the server.
    /// </summary>
    public interface IDamageable
    {
        bool IsAlive { get; }

        void ApplyDamage(in DamageInfo damage);
    }
}
