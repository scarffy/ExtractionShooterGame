namespace ExtractionShooter.UI.Inventory
{
    /// <summary>
    /// Pixel geometry of grid inventories, shared by the grid views and the drag ghost.
    /// Keep in step with the <c>.grid-cell</c> / <c>.grid-item</c> rules in InventoryScreen.uss.
    /// </summary>
    internal static class GridMetrics
    {
        /// <summary>Distance from one cell to the next (cell plus gap), in panel pixels.</summary>
        public const float CellPitch = 64f;

        /// <summary>Space between neighbouring cells and items.</summary>
        public const float CellGap = 4f;

        /// <summary>Drawn size of one cell.</summary>
        public const float CellSize = CellPitch - CellGap;

        /// <summary>Drawn size of something spanning <paramref name="cells"/> cells.</summary>
        public static float Span(int cells) => cells * CellPitch - CellGap;
    }
}
