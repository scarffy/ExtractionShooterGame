namespace ExtractionShooter.Core.Throwables
{
    /// <summary>What a thrown grenade does when its fuse runs out. Sent over the network as a byte.</summary>
    public enum ThrowableKind : byte
    {
        /// <summary>Explodes: damage with distance falloff to everyone it can see; loud.</summary>
        Frag = 0,

        /// <summary>Releases a cloud that blocks sight (bots cannot see through it) for a while.</summary>
        Smoke = 1,

        /// <summary>Stun grenade: whites out the screens of players looking at it and blinds bots.</summary>
        Flash = 2,
    }
}
