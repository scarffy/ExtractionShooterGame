namespace ExtractionShooter.Gameplay.Visuals
{
    /// <summary>
    /// The primitive model an item is drawn with on a character (and in the first-person view). Picked from the item's
    /// type and numbers by <see cref="VisualClassifier"/>, or set per item id in a <see cref="VisualProfile"/>.
    /// Values are serialized in profiles: append new shapes, never reorder.
    /// </summary>
    public enum GearShape
    {
        /// <summary>Not drawn (or: use the default for the item).</summary>
        None = 0,

        HelmetLight = 1,
        HelmetHeavy = 2,
        SoftVest = 3,
        PlateCarrier = 4,
        RigSmall = 5,
        RigLarge = 6,
        BackpackSmall = 7,
        BackpackLarge = 8,

        Pistol = 20,
        Smg = 21,
        Rifle = 22,
        Shotgun = 23,
        Marksman = 24,
        Sniper = 25,

        RedDot = 40,
        Holo = 41,
        Scope = 42,
        Suppressor = 43,
        Compensator = 44,
        VerticalGrip = 45,
        AngledGrip = 46,
        ExtendedMag = 47,

        Knife = 60,
    }

    /// <summary>How a held weapon is gripped: decides the arm pose and stance.</summary>
    public enum HoldStyle
    {
        /// <summary>No weapon in hand: arms swing.</summary>
        None = 0,

        /// <summary>One hand on the pistol grip, the other on the handguard, stock in the shoulder.</summary>
        Long = 1,

        /// <summary>Both hands on the grip, arms forward.</summary>
        Pistol = 2,
    }
}
