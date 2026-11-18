// Fixed-window rate limiter keyed by string (IP address or server id). Pure: time comes from the clock.
// Each key's window starts at its first hit and lasts windowMs; at most `limit` hits fit in one window.

/**
 * @param {object} opts
 * @param {number} opts.limit     hits allowed per window
 * @param {number} opts.windowMs  window length
 * @param {number} [opts.maxKeys] memory cap; oldest keys are dropped beyond it
 * @param {{ now(): number }} [opts.clock]
 */
export function createRateLimiter({ limit, windowMs, maxKeys = 10000, clock = { now: () => Date.now() } }) {
  /** @type {Map<string, { start: number, count: number }>} */
  const windows = new Map();

  /** Records a hit. Returns { allowed, retryAfterSeconds }. */
  function hit(key) {
    const now = clock.now();
    let w = windows.get(key);
    if (!w || now - w.start >= windowMs) {
      if (!w && windows.size >= maxKeys) evict();
      w = { start: now, count: 0 };
      windows.set(key, w);
    }
    if (w.count >= limit) {
      return { allowed: false, retryAfterSeconds: Math.max(1, Math.ceil((w.start + windowMs - now) / 1000)) };
    }
    w.count += 1;
    return { allowed: true, retryAfterSeconds: 0 };
  }

  /** Drops windows that have ended. */
  function prune() {
    const now = clock.now();
    for (const [key, w] of windows) {
      if (now - w.start >= windowMs) windows.delete(key);
    }
  }

  function evict() {
    prune();
    // Still full: drop the oldest key (Map keeps insertion order).
    if (windows.size >= maxKeys) windows.delete(windows.keys().next().value);
  }

  return { hit, prune, size: () => windows.size };
}
