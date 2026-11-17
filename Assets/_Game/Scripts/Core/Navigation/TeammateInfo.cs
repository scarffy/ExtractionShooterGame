namespace ExtractionShooter.Core.Navigation
{
    /// <summary>One squad member as the map and tactical panel show it. Positions are on the ground plane.</summary>
    public readonly struct TeammateInfo
    {
        public TeammateInfo(string name, bool isLocal, bool isAlive, float health01, float worldX, float worldZ, float bearing)
        {
            Name = name;
            IsLocal = isLocal;
            IsAlive = isAlive;
            Health01 = health01 < 0f ? 0f : health01 > 1f ? 1f : health01;
            WorldX = worldX;
            WorldZ = worldZ;
            Bearing = bearing;
        }

        public string Name { get; }

        /// <summary>The player of this client.</summary>
        public bool IsLocal { get; }

        public bool IsAlive { get; }

        /// <summary>Health as a share of maximum, 0..1.</summary>
        public float Health01 { get; }

        public float WorldX { get; }
        public float WorldZ { get; }

        /// <summary>Facing as a compass bearing (0 = north).</summary>
        public float Bearing { get; }
    }
}
