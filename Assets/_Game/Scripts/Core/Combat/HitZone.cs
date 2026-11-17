namespace ExtractionShooter.Core.Combat
{
    /// <summary>Body region a hit landed on. Armor protects specific zones.</summary>
    public enum HitZone
    {
        Body = 0,
        Head = 1,

        /// <summary>Hips and legs: no armor; heavy hits can fracture a leg.</summary>
        Legs = 2,

        /// <summary>Bleeding and other damage from inside: skips armor and never causes new injuries.</summary>
        Internal = 3,
    }
}
