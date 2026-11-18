using System;
using System.Collections.Generic;

namespace ExtractionShooter.Core.Squad
{
    /// <summary>
    /// The squad's live pings, oldest first. Enforces the per-player cap (a player's newest ping replaces their oldest
    /// once they have <see cref="MaxPerOwner"/>) and drops pings when they expire. The server keeps one per player to
    /// decide what is live; every client keeps one for the HUD and runs the same rules, so both agree on what is shown.
    /// Pure logic, unit-tested.
    /// </summary>
    public sealed class PingBoard
    {
        private readonly List<SquadPing> _pings = new List<SquadPing>();

        public PingBoard(int maxPerOwner = PingRules.MaxActivePerPlayer)
        {
            MaxPerOwner = Math.Max(1, maxPerOwner);
        }

        public int MaxPerOwner { get; }

        /// <summary>Live pings, oldest first.</summary>
        public IReadOnlyList<SquadPing> Active => _pings;

        public int Count => _pings.Count;

        /// <summary>Goes up on every change, so views can skip work when nothing moved.</summary>
        public int Revision { get; private set; }

        /// <summary>
        /// Adds a ping, or replaces the one with the same id in place (a danger upgrade). Adding beyond the owner's cap
        /// removes their oldest pings; their ids go to <paramref name="evicted"/> when given.
        /// </summary>
        public void Upsert(in SquadPing ping, List<int> evicted = null)
        {
            Revision++;
            int existing = IndexOf(ping.Id);
            if (existing >= 0)
            {
                _pings[existing] = ping;
                return;
            }

            _pings.Add(ping);
            int owned = CountFor(ping.OwnerId);
            for (int i = 0; i < _pings.Count && owned > MaxPerOwner; i++)
            {
                if (_pings[i].OwnerId != ping.OwnerId || _pings[i].Id == ping.Id) continue;
                evicted?.Add(_pings[i].Id);
                _pings.RemoveAt(i);
                i--;
                owned--;
            }
        }

        /// <summary>Removes a ping by id. Returns whether it was there.</summary>
        public bool Remove(int id)
        {
            int index = IndexOf(id);
            if (index < 0) return false;
            _pings.RemoveAt(index);
            Revision++;
            return true;
        }

        /// <summary>Drops every ping that has expired by <paramref name="now"/>. Returns how many went.</summary>
        public int Prune(double now)
        {
            int removed = _pings.RemoveAll(p => p.IsExpired(now));
            if (removed > 0) Revision++;
            return removed;
        }

        public void Clear()
        {
            if (_pings.Count == 0) return;
            _pings.Clear();
            Revision++;
        }

        public bool TryGet(int id, out SquadPing ping)
        {
            int index = IndexOf(id);
            ping = index >= 0 ? _pings[index] : default;
            return index >= 0;
        }

        /// <summary>How many live pings a player has.</summary>
        public int CountFor(int ownerId)
        {
            int count = 0;
            foreach (SquadPing ping in _pings)
                if (ping.OwnerId == ownerId) count++;
            return count;
        }

        private int IndexOf(int id)
        {
            for (int i = 0; i < _pings.Count; i++)
                if (_pings[i].Id == id) return i;
            return -1;
        }
    }
}
