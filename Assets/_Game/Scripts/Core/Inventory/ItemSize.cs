using System;

namespace ExtractionShooter.Core.Inventory
{
    /// <summary>How many grid cells an item covers, unrotated (width across, height down).</summary>
    [Serializable]
    public readonly struct ItemSize : IEquatable<ItemSize>
    {
        public readonly int Width;
        public readonly int Height;

        public ItemSize(int width, int height)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
        }

        public static ItemSize One => new ItemSize(1, 1);

        public int Area => Width * Height;

        /// <summary>The footprint turned 90°: width and height swapped.</summary>
        public ItemSize Rotated => new ItemSize(Height, Width);

        /// <summary>True when rotating changes nothing (square items).</summary>
        public bool IsSquare => Width == Height;

        public ItemSize Oriented(bool rotated) => rotated ? Rotated : this;

        public bool Equals(ItemSize other) => Width == other.Width && Height == other.Height;
        public override bool Equals(object obj) => obj is ItemSize other && Equals(other);
        public override int GetHashCode() => Width * 397 ^ Height;
        public override string ToString() => $"{Width}x{Height}";
    }
}
