namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>Who decides whether a player may use a container.</summary>
    public enum ContainerAccess
    {
        /// <summary>Carried by a player (equipment, bag storage). Access is decided by <see cref="PlayerInventory"/>.</summary>
        Carried = 0,

        /// <summary>In the world (crates, corpses): any living player within reach.</summary>
        World = 1,
    }
}
