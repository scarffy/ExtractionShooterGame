using System;
using System.Collections.Generic;
using System.Text;

namespace ExtractionShooter.Core.Inventory
{
    /// <summary>Where an attachment goes on a weapon.</summary>
    public enum AttachmentSlot : byte
    {
        Optic = 0,
        Muzzle = 1,
        Grip = 2,
        Magazine = 3,
    }

    /// <summary>
    /// The attachments fitted to one weapon, stored on its <see cref="ItemStack.Attachments"/> as a short string
    /// ("optic:red_dot;grip:vertical_grip"). One item per slot. Pure logic, unit-tested.
    /// </summary>
    public static class WeaponMods
    {
        public const int SlotCount = 4;
        private const char PairSeparator = ';';
        private const char KeySeparator = ':';

        private static readonly string[] SlotKeys = { "optic", "muzzle", "grip", "mag" };

        /// <summary>Item id in a slot, or null.</summary>
        public static string Get(string attachments, AttachmentSlot slot)
        {
            if (string.IsNullOrEmpty(attachments)) return null;
            string key = SlotKeys[(int)slot];
            foreach (string pair in attachments.Split(PairSeparator))
            {
                int colon = pair.IndexOf(KeySeparator);
                if (colon > 0 && string.Equals(pair.Substring(0, colon), key, StringComparison.Ordinal))
                {
                    string id = pair.Substring(colon + 1);
                    return id.Length > 0 ? id : null;
                }
            }
            return null;
        }

        /// <summary>A copy with the slot set to <paramref name="itemId"/> (null or empty clears it). Canonical order.</summary>
        public static string Set(string attachments, AttachmentSlot slot, string itemId)
        {
            var ids = new string[SlotCount];
            for (int i = 0; i < SlotCount; i++) ids[i] = Get(attachments, (AttachmentSlot)i);
            ids[(int)slot] = string.IsNullOrEmpty(itemId) ? null : Sanitize(itemId);
            return Format(ids);
        }

        /// <summary>Every fitted (slot, item id), in slot order.</summary>
        public static IEnumerable<KeyValuePair<AttachmentSlot, string>> All(string attachments)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                string id = Get(attachments, (AttachmentSlot)i);
                if (id != null) yield return new KeyValuePair<AttachmentSlot, string>((AttachmentSlot)i, id);
            }
        }

        /// <summary>Canonical form, dropping anything unknown or empty (for loading saved data).</summary>
        public static string Normalize(string attachments, Func<string, bool> isKnownItem = null)
        {
            var ids = new string[SlotCount];
            for (int i = 0; i < SlotCount; i++)
            {
                string id = Get(attachments, (AttachmentSlot)i);
                if (id != null && (isKnownItem == null || isKnownItem(id))) ids[i] = Sanitize(id);
            }
            return Format(ids);
        }

        private static string Format(string[] ids)
        {
            var text = new StringBuilder();
            for (int i = 0; i < SlotCount; i++)
            {
                if (string.IsNullOrEmpty(ids[i])) continue;
                if (text.Length > 0) text.Append(PairSeparator);
                text.Append(SlotKeys[i]).Append(KeySeparator).Append(ids[i]);
            }
            return text.Length > 0 ? text.ToString() : null;
        }

        private static string Sanitize(string id) => id.Replace(PairSeparator.ToString(), string.Empty).Replace(KeySeparator.ToString(), string.Empty);
    }
}
