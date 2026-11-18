// In-memory server registry with TTL expiry and a size cap. Pure: time comes from the injected clock.

/**
 * @param {object} opts
 * @param {number} opts.ttlMs       entries not refreshed for this long are removed
 * @param {number} opts.maxEntries  new ids are refused when this many live entries exist
 * @param {{ now(): number }} [opts.clock]
 * @param {(server: object) => void} [opts.onExpire] called for each entry removed by expiry
 */
export function createRegistry({ ttlMs, maxEntries, clock = { now: () => Date.now() }, onExpire = () => {} }) {
  /** @type {Map<string, { server: object, lastSeen: number }>} */
  const entries = new Map();

  /** Removes expired entries and returns the removed server objects. */
  function prune() {
    const cutoff = clock.now() - ttlMs;
    const removed = [];
    for (const [id, entry] of entries) {
      if (entry.lastSeen <= cutoff) {
        entries.delete(id);
        removed.push(entry.server);
        onExpire(entry.server);
      }
    }
    return removed;
  }

  /**
   * Inserts or refreshes a server.
   * Returns { ok: true, created } or { ok: false, error: 'full' }.
   */
  function upsert(server) {
    prune();
    const created = !entries.has(server.id);
    if (created && entries.size >= maxEntries) return { ok: false, error: 'full' };
    entries.set(server.id, { server: { ...server }, lastSeen: clock.now() });
    return { ok: true, created };
  }

  /** Removes a server; returns true if it existed. */
  function remove(id) {
    return entries.delete(id);
  }

  /** Live servers, optionally filtered, sorted by players desc then name, with lastSeenSecondsAgo. */
  function list({ version, region } = {}) {
    prune();
    const now = clock.now();
    const out = [];
    for (const { server, lastSeen } of entries.values()) {
      if (version !== undefined && server.version !== version) continue;
      if (region !== undefined && server.region !== region) continue;
      out.push({ ...server, lastSeenSecondsAgo: Math.max(0, Math.floor((now - lastSeen) / 1000)) });
    }
    out.sort((a, b) => b.players - a.players || compareText(a.name, b.name) || compareText(a.id, b.id));
    return out;
  }

  function size() {
    prune();
    return entries.size;
  }

  return { upsert, remove, list, size, prune };
}

function compareText(a, b) {
  const la = a.toLowerCase();
  const lb = b.toLowerCase();
  if (la !== lb) return la < lb ? -1 : 1;
  return a < b ? -1 : a > b ? 1 : 0;
}
