import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRateLimiter } from '../src/rateLimit.js';
import { fakeClock } from './helpers.js';

test('allows up to limit per window, then blocks with Retry-After', () => {
  const clock = fakeClock();
  const rl = createRateLimiter({ limit: 3, windowMs: 60_000, clock });
  for (let i = 0; i < 3; i++) assert.equal(rl.hit('ip').allowed, true);
  const blocked = rl.hit('ip');
  assert.equal(blocked.allowed, false);
  assert.equal(blocked.retryAfterSeconds, 60);
  clock.advance(59_500);
  assert.deepEqual(rl.hit('ip'), { allowed: false, retryAfterSeconds: 1 });
  clock.advance(500);
  assert.equal(rl.hit('ip').allowed, true);
});

test('keys are independent', () => {
  const rl = createRateLimiter({ limit: 1, windowMs: 2000, clock: fakeClock() });
  assert.equal(rl.hit('a').allowed, true);
  assert.equal(rl.hit('b').allowed, true);
  assert.equal(rl.hit('a').allowed, false);
});

test('1 per 2 s per id', () => {
  const clock = fakeClock();
  const rl = createRateLimiter({ limit: 1, windowMs: 2000, clock });
  assert.equal(rl.hit('id').allowed, true);
  clock.advance(1999);
  assert.equal(rl.hit('id').allowed, false);
  clock.advance(1);
  assert.equal(rl.hit('id').allowed, true);
});

test('prune drops finished windows; maxKeys caps memory', () => {
  const clock = fakeClock();
  const rl = createRateLimiter({ limit: 1, windowMs: 1000, maxKeys: 3, clock });
  rl.hit('a');
  rl.hit('b');
  rl.hit('c');
  rl.hit('d'); // evicts oldest ('a')
  assert.equal(rl.size(), 3);
  assert.equal(rl.hit('a').allowed, true); // 'a' was forgotten, so it starts fresh
  clock.advance(1000);
  rl.prune();
  assert.equal(rl.size(), 0);
});
