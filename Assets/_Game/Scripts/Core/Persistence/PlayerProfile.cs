using System;
using System.Collections.Generic;

namespace ExtractionShooter.Core.Persistence
{
    /// <summary>
    /// Everything a player keeps between raids: the stash, the equipped loadout and pockets, the safe box and credits.
    /// Plain serializable data (Unity's JsonUtility compatible: public fields, no recursion).
    /// Bag contents are stored as separate containers referenced by key, so nesting stays flat.
    /// </summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        /// <summary>2 added <see cref="Money"/>; 3 added <see cref="StashTier"/>, <see cref="Crafts"/> and <see cref="Presets"/>.</summary>
        public const int CurrentVersion = 3;

        public int Version = CurrentVersion;
        public string PlayerName;

        /// <summary>Credits, earned and spent at the trader. Profiles saved before version 2 load with none.</summary>
        public long Money;
        public StoredContainer Stash = new StoredContainer();
        public StoredContainer Loadout = new StoredContainer();

        /// <summary>The loadout's pockets.</summary>
        public StoredContainer Pockets = new StoredContainer();

        /// <summary>Contents of the safe box (kept through death).</summary>
        public StoredContainer SafeBox = new StoredContainer();

        /// <summary>Index into the trader's safe box sizes; 0 is the free box.</summary>
        public int SafeBoxTier;
        public List<StoredContainer> Bags = new List<StoredContainer>();

        /// <summary>Bought stash size level; 0 is the starting stash. Profiles saved before version 3 load with 0.</summary>
        public int StashTier;

        /// <summary>Workbench crafts in progress or waiting to be collected. Null or missing in old profiles.</summary>
        public List<StoredCraft> Crafts = new List<StoredCraft>();

        /// <summary>Saved gun bench presets. Null or missing in old profiles.</summary>
        public List<StoredPreset> Presets = new List<StoredPreset>();

        /// <summary>Finds the stored contents of a bag, or null.</summary>
        public StoredContainer FindBag(string key)
        {
            if (string.IsNullOrEmpty(key) || Bags == null) return null;
            foreach (StoredContainer bag in Bags)
                if (bag != null && bag.Key == key) return bag;
            return null;
        }
    }

    /// <summary>The items of one container, by slot.</summary>
    [Serializable]
    public sealed class StoredContainer
    {
        public string Key;
        public List<StoredItem> Items = new List<StoredItem>();
    }

    /// <summary>One stack in a slot, with its per-instance state.</summary>
    [Serializable]
    public sealed class StoredItem
    {
        public int Slot;
        public string ItemId;
        public int Quantity;
        public int Durability;
        public string AmmoId;
        public int AmmoCount;

        /// <summary>Turned 90° in its grid.</summary>
        public bool Rotated;

        /// <summary>Weapons: fitted attachments (WeaponMods format).</summary>
        public string Attachments;

        /// <summary>Key of the bag's contents in <see cref="PlayerProfile.Bags"/>; empty for non-bags.</summary>
        public string ContentsKey;
    }

    /// <summary>A queued workbench craft: the recipe and when it started and finishes (UTC ticks).</summary>
    [Serializable]
    public sealed class StoredCraft
    {
        public string RecipeId;
        public long StartUtcTicks;
        public long FinishUtcTicks;
    }

    /// <summary>A saved gun bench preset: name, weapon id and attachments (WeaponMods format).</summary>
    [Serializable]
    public sealed class StoredPreset
    {
        public string Name;
        public string WeaponId;
        public string Attachments;
    }
}
