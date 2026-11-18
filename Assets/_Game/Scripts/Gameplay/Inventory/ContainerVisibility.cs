namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>Which peers receive a container's contents (see <see cref="ItemContainer"/> for the full rule).</summary>
    public enum ContainerVisibility
    {
        /// <summary>
        /// Only peers allowed to see it: the owner of the container's network object (or of the object holding the
        /// bag this storage belongs to), plus peers the server grants a view (looting). The default, and the safe one.
        /// </summary>
        Private = 0,

        /// <summary>Every observer (a player's worn equipment: other clients draw the gear and the drawn weapon).</summary>
        Public = 1,
    }
}
