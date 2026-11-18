import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRegistry } from '../src/registry.js';
import { fakeClock } from './helpers.js';

const server = (id, extra = {}) => ({ id, name: id, players: 0, version: '1', region: 'sg', ...extra });

test('upsert creates then refreshes', () => {
  const reg = createRegistry({ ttlMs: 30_000, maxEntries: 10, clock: fakeClock() });
  assert.deepEqual(reg.upsert(server('aaaaaaaa')), { ok: true, created: true });
  assert.deepEqual(reg.upsert(server('aaaaaaaa', { players: 2 })), { ok: true, created: false });
  assert.equal(reg.size(), 1);
  assert.equal(reg.list()[0].players, 2);
});

test('entries expire after ttl and lastSeenSecondsAgo counts up', () => {
  const clock = fakeClock();
  const expired = [];
  const reg = createRegistry({ ttlMs: 30_000, maxEntries: 10, clock, onExpire: (s) => expired.push(s.id) });
  reg.upsert(server('aaaaaaaa'));
  clock.advance(4_500);
  assert.equal(reg.list()[0].lastSeenSecondsAgo, 4);
  clock.advance(25_499); // 29.999 s
  assert.equal(reg.size(), 1);
  clock.advance(1);      // exactly 30 s
  assert.equal(reg.size(), 0);
  assert.deepEqual(expired, ['aaaaaaaa']);
});

test('refreshing resets the ttl', () => {
  const clock = fakeClock();
  const reg = createRegistry({ ttlMs: 30_000, maxEntries: 10, clock });
  reg.upsert(server('aaaaaaaa'));
  clock.advance(20_000);
  reg.upsert(server('aaaaaaaa'));
  clock.advance(20_000);
  assert.equal(reg.size(), 1);
});

test('max entries: new ids refused when full, existing ids still update, expiry frees space', () => {
  const clock = fakeClock();
  const reg = createRegistry({ ttlMs: 30_000, maxEntries: 2, clock });
  reg.upsert(server('aaaaaaaa'));
  clock.advance(10_000);
  reg.upsert(server('bbbbbbbb'));
  assert.deepEqual(reg.upsert(server('cccccccc')), { ok: false, error: 'full' });
  assert.equal(reg.upsert(server('bbbbbbbb')).ok, true);
  clock.advance(20_000); // aaaaaaaa expires
  assert.deepEqual(reg.upsert(server('cccccccc')), { ok: true, created: true });
});

test('remove', () => {
  const reg = createRegistry({ ttlMs: 30_000, maxEntries: 10, clock: fakeClock() });
  reg.upsert(server('aaaaaaaa'));
  assert.equal(reg.remove('aaaaaaaa'), true);
  assert.equal(reg.remove('aaaaaaaa'), false);
  assert.equal(reg.size(), 0);
});

test('list filters by version and region and sorts by players desc then name', () => {
  const reg = createRegistry({ ttlMs: 30_000, maxEntries: 10, clock: fakeClock() });
  reg.upsert(server('id-00001', { name: 'bravo', players: 2 }));
  reg.upsert(server('id-00002', { name: 'Alpha', players: 2 }));
  reg.upsert(server('id-00003', { name: 'charlie', players: 5 }));
  reg.upsert(server('id-00004', { name: 'delta', players: 9, version: '2' }));
  reg.upsert(server('id-00005', { name: 'echo', players: 1, region: 'my-kl' }));
  assert.deepEqual(reg.list().map((s) => s.name), ['delta', 'charlie', 'Alpha', 'bravo', 'echo']);
  assert.deepEqual(reg.list({ version: '1' }).map((s) => s.name), ['charlie', 'Alpha', 'bravo', 'echo']);
  assert.deepEqual(reg.list({ region: 'my-kl' }).map((s) => s.name), ['echo']);
  assert.deepEqual(reg.list({ version: '2', region: 'my-kl' }), []);
});

test('stored entries are copies (caller mutations do not leak in)', () => {
  const reg = createRegistry({ ttlMs: 30_000, maxEntries: 10, clock: fakeClock() });
  const s = server('aaaaaaaa');
  reg.upsert(s);
  s.name = 'changed';
  reg.list()[0].name = 'changed too';
  assert.equal(reg.list()[0].name, 'aaaaaaaa');
});
