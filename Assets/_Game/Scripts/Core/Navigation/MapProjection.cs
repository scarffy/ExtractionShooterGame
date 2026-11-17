using System;

namespace ExtractionShooter.Core.Navigation
{
    /// <summary>
    /// Top-down projection of a rectangle of the ground plane (X east, Z north) onto a screen rectangle
    /// (x right, y down), north up, uniformly scaled to fit and centred. Map pixels are relative to the
    /// view rectangle's top-left corner.
    /// </summary>
    public readonly struct MapProjection
    {
        public MapProjection(float worldMinX, float worldMinZ, float worldSizeX, float worldSizeZ,
            float scale, float offsetX, float offsetY)
        {
            WorldMinX = worldMinX;
            WorldMinZ = worldMinZ;
            WorldSizeX = worldSizeX;
            WorldSizeZ = worldSizeZ;
            Scale = scale;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        public float WorldMinX { get; }
        public float WorldMinZ { get; }
        public float WorldSizeX { get; }
        public float WorldSizeZ { get; }

        /// <summary>Map pixels per world metre.</summary>
        public float Scale { get; }

        /// <summary>Left edge of the projected world rectangle inside the view.</summary>
        public float OffsetX { get; }

        /// <summary>Top edge of the projected world rectangle inside the view.</summary>
        public float OffsetY { get; }

        /// <summary>Width of the projected world rectangle, in pixels.</summary>
        public float MapWidth => WorldSizeX * Scale;

        /// <summary>Height of the projected world rectangle, in pixels.</summary>
        public float MapHeight => WorldSizeZ * Scale;

        public bool IsValid => Scale > 0f && WorldSizeX > 0f && WorldSizeZ > 0f;

        /// <summary>
        /// Fits a world rectangle into a view, keeping its aspect ratio, with <paramref name="padding"/> pixels
        /// kept free on every side. Degenerate input gives an invalid projection (<see cref="IsValid"/> false).
        /// </summary>
        public static MapProjection Fit(float worldMinX, float worldMinZ, float worldSizeX, float worldSizeZ,
            float viewWidth, float viewHeight, float padding = 0f)
        {
            float usableWidth = viewWidth - padding * 2f, usableHeight = viewHeight - padding * 2f;
            if (worldSizeX <= 0f || worldSizeZ <= 0f || usableWidth <= 0f || usableHeight <= 0f)
                return new MapProjection(worldMinX, worldMinZ, worldSizeX, worldSizeZ, 0f, 0f, 0f);

            float scale = Math.Min(usableWidth / worldSizeX, usableHeight / worldSizeZ);
            float offsetX = (viewWidth - worldSizeX * scale) * 0.5f;
            float offsetY = (viewHeight - worldSizeZ * scale) * 0.5f;
            return new MapProjection(worldMinX, worldMinZ, worldSizeX, worldSizeZ, scale, offsetX, offsetY);
        }

        /// <summary>Map pixel of a world point (north is up).</summary>
        public void WorldToMap(float worldX, float worldZ, out float mapX, out float mapY)
        {
            mapX = OffsetX + (worldX - WorldMinX) * Scale;
            mapY = OffsetY + (WorldMinZ + WorldSizeZ - worldZ) * Scale;
        }

        /// <summary>World point under a map pixel (inverse of <see cref="WorldToMap"/>); the origin when invalid.</summary>
        public void MapToWorld(float mapX, float mapY, out float worldX, out float worldZ)
        {
            if (Scale <= 0f)
            {
                worldX = 0f;
                worldZ = 0f;
                return;
            }
            worldX = WorldMinX + (mapX - OffsetX) / Scale;
            worldZ = WorldMinZ + WorldSizeZ - (mapY - OffsetY) / Scale;
        }

        /// <summary>
        /// Screen rectangle (left, top, width, height) of an axis-aligned world rectangle given by its centre and size.
        /// </summary>
        public void WorldRectToMap(float centerX, float centerZ, float sizeX, float sizeZ,
            out float left, out float top, out float width, out float height)
        {
            WorldToMap(centerX - sizeX * 0.5f, centerZ + sizeZ * 0.5f, out left, out top);
            width = Math.Abs(sizeX) * Scale;
            height = Math.Abs(sizeZ) * Scale;
        }

        /// <summary>Whether a world point lies inside the mapped area.</summary>
        public bool Contains(float worldX, float worldZ) =>
            worldX >= WorldMinX && worldX <= WorldMinX + WorldSizeX &&
            worldZ >= WorldMinZ && worldZ <= WorldMinZ + WorldSizeZ;
    }
}
