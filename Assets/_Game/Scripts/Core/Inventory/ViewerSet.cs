using System;
using System.Collections.Generic;

namespace ExtractionShooter.Core.Inventory
{
    /// <summary>
    /// Server bookkeeping for who may see a private container's contents, and who currently holds a copy.
    /// The audience is the container's owner (if any) plus every viewer with at least one grant (grants are counted,
    /// so two independent reasons to see a container never cancel each other out). <see cref="Update"/> works out
    /// who must be sent a full copy and who must be told to drop theirs.
    /// Pure C#, generic over the peer type, so it is unit-testable without networking.
    /// </summary>
    public sealed class ViewerSet<T>
    {
        private readonly Dictionary<T, int> _grants;
        private readonly HashSet<T> _current;
        private readonly HashSet<T> _desired;
        private readonly IEqualityComparer<T> _comparer;

        public ViewerSet(IEqualityComparer<T> comparer = null)
        {
            _comparer = comparer ?? EqualityComparer<T>.Default;
            _grants = new Dictionary<T, int>(_comparer);
            _current = new HashSet<T>(_comparer);
            _desired = new HashSet<T>(_comparer);
        }

        /// <summary>Peers that currently hold a copy (were sent one and not told to drop it).</summary>
        public IReadOnlyCollection<T> Current => _current;

        /// <summary>Number of peers with at least one grant.</summary>
        public int GrantedCount => _grants.Count;

        public bool HasCopy(T peer) => peer != null && _current.Contains(peer);

        public bool IsGranted(T peer) => peer != null && _grants.ContainsKey(peer);

        /// <summary>Adds one grant for a peer. Takes effect at the next <see cref="Update"/>.</summary>
        public void Grant(T peer)
        {
            if (peer == null) return;
            _grants.TryGetValue(peer, out int count);
            _grants[peer] = count + 1;
        }

        /// <summary>Removes one grant. Returns true when the peer has no grant left. Takes effect at the next <see cref="Update"/>.</summary>
        public bool Revoke(T peer)
        {
            if (peer == null || !_grants.TryGetValue(peer, out int count)) return false;
            if (count > 1)
            {
                _grants[peer] = count - 1;
                return false;
            }
            _grants.Remove(peer);
            return true;
        }

        /// <summary>
        /// The peer no longer holds a copy without being told to drop it (the object was despawned for it, or is
        /// about to be spawned for it again). Grants are kept; the next <see cref="Update"/> sends a fresh copy if
        /// the peer is still in the audience.
        /// </summary>
        public void Forget(T peer)
        {
            if (peer != null) _current.Remove(peer);
        }

        /// <summary>
        /// Recomputes the audience: <paramref name="owner"/> (when <paramref name="hasOwner"/>) plus every granted peer,
        /// keeping only peers <paramref name="canReceive"/> accepts. <paramref name="added"/> gets the peers that need
        /// a full copy, <paramref name="removed"/> the ones that must drop theirs (both cleared first).
        /// Returns true if anything changed.
        /// </summary>
        public bool Update(T owner, bool hasOwner, Func<T, bool> canReceive, List<T> added, List<T> removed)
        {
            if (added == null) throw new ArgumentNullException(nameof(added));
            if (removed == null) throw new ArgumentNullException(nameof(removed));
            added.Clear();
            removed.Clear();

            _desired.Clear();
            if (hasOwner && owner != null && (canReceive == null || canReceive(owner))) _desired.Add(owner);
            foreach (T peer in _grants.Keys)
                if (canReceive == null || canReceive(peer)) _desired.Add(peer);

            foreach (T peer in _current)
                if (!_desired.Contains(peer)) removed.Add(peer);
            foreach (T peer in _desired)
                if (!_current.Contains(peer)) added.Add(peer);

            foreach (T peer in removed) _current.Remove(peer);
            foreach (T peer in added) _current.Add(peer);
            return added.Count > 0 || removed.Count > 0;
        }

        /// <summary>Forgets everything (grants and copies), e.g. when the container stops on the server.</summary>
        public void Reset()
        {
            _grants.Clear();
            _current.Clear();
            _desired.Clear();
        }
    }
}
