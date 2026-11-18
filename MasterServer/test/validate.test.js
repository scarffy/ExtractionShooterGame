import { test } from 'node:test';
import assert from 'node:assert/strict';
import { validateHeartbeat, normalizeAddress, isValidHost, clientAddress } from '../src/validate.js';
import { heartbeat } from './helpers.js';

const errorOf = (body) => {
  const r = validateHeartbeat(body);
  assert.equal(r.ok, false, `expected rejection for ${JSON.stringify(body)}`);
  return r.error;
};

test('accepts the contract example and keeps only known fields', () => {
  const r = validateHeartbeat({ ...heartbeat({ id: '6f1c2a9e0b5d4c3a8e7f6a5b4c3d2e1f' }), extra: 'ignored' });
  assert.equal(r.ok, true);
  assert.equal(r.value.id, '6f1c2a9e0b5d4c3a8e7f6a5b4c3d2e1f');
  assert.equal('extra' in r.value, false);
  assert.deepEqual(Object.keys(r.value).sort(), [
    'difficulties', 'host', 'id', 'map', 'maxPlayers', 'name', 'players', 'port', 'raidDifficulty',
    'raidRunning', 'raidSecondsLeft', 'region', 'version',
  ]);
});

test('rejects non-object bodies', () => {
  for (const b of [null, undefined, 'x', 5, [heartbeat()]]) assert.match(errorOf(b), /^body: /);
});

test('id rules', () => {
  for (const id of [undefined, 1234567890, 'short', 'a'.repeat(65), 'has space!', 'ünïcode-id']) {
    assert.match(errorOf(heartbeat({ id })), /^id: /);
  }
  for (const id of ['abcdefgh', 'a'.repeat(64), 'A-b_C-0123']) assert.equal(validateHeartbeat(heartbeat({ id })).ok, true);
});

test('name rules: trimmed, 1-40 code points, no control chars', () => {
  assert.equal(validateHeartbeat(heartbeat({ name: '  Kim  ' })).value.name, 'Kim');
  assert.equal(validateHeartbeat(heartbeat({ name: '🔥'.repeat(40) })).ok, true);
  for (const name of [undefined, 42, '', '   ', 'x'.repeat(41), 'bad\nname', 'tab\there', 'del\u007F']) {
    assert.match(errorOf(heartbeat({ name })), /^name: /);
  }
});

test('host rules', () => {
  const ok = (host) => validateHeartbeat(heartbeat({ host }));
  assert.equal(ok(undefined).value.host, '');
  assert.equal(ok(null).value.host, '');
  assert.equal(ok('').value.host, '');
  assert.equal(ok(' game.example.com ').value.host, 'game.example.com');
  assert.equal(ok('203.0.113.10').value.host, '203.0.113.10');
  assert.equal(ok('2001:db8::1').value.host, '2001:db8::1');
  assert.equal(ok('::ffff:203.0.113.10').value.host, '203.0.113.10');
  for (const host of [5, 'bad host', '-bad.example.com', 'a'.repeat(254), 'under_score.com', 'x..y', 'http://x']) {
    assert.match(errorOf(heartbeat({ host })), /^host: /);
  }
});

test('port rules', () => {
  for (const port of [undefined, 0, 65536, 7770.5, '7770', -1]) assert.match(errorOf(heartbeat({ port })), /^port: /);
  assert.equal(validateHeartbeat(heartbeat({ port: 1 })).ok, true);
  assert.equal(validateHeartbeat(heartbeat({ port: 65535 })).ok, true);
});

test('region rules (optional, may be empty)', () => {
  assert.equal(validateHeartbeat(heartbeat({ region: '' })).value.region, '');
  assert.equal(validateHeartbeat(heartbeat({ region: undefined })).value.region, '');
  assert.equal(validateHeartbeat(heartbeat({ region: 'my-kl' })).value.region, 'my-kl');
  for (const region of ['SG', 'sg_1', 'a'.repeat(17), 3]) assert.match(errorOf(heartbeat({ region })), /^region: /);
});

test('map and difficulties rules', () => {
  for (const map of [undefined, '', 'm'.repeat(33), 'a\nb', 7]) assert.match(errorOf(heartbeat({ map })), /^map: /);
  for (const difficulties of [undefined, 'Easy', 'easynormal', 'EasyHard']) {
    assert.match(errorOf(heartbeat({ difficulties })), /^difficulties: /);
  }
  assert.equal(validateHeartbeat(heartbeat({ difficulties: 'NormalHard' })).ok, true);
});

test('players and maxPlayers rules', () => {
  for (const maxPlayers of [undefined, 0, 129, 1.5]) assert.match(errorOf(heartbeat({ maxPlayers })), /^maxPlayers: /);
  for (const players of [undefined, -1, 13, 1.5, '3']) assert.match(errorOf(heartbeat({ players })), /^players: /);
  assert.equal(validateHeartbeat(heartbeat({ players: 0 })).ok, true);
  assert.equal(validateHeartbeat(heartbeat({ players: 12, maxPlayers: 12 })).ok, true);
  assert.equal(validateHeartbeat(heartbeat({ players: 128, maxPlayers: 128 })).ok, true);
});

test('raid fields rules', () => {
  for (const raidRunning of [undefined, 1, 'true']) assert.match(errorOf(heartbeat({ raidRunning })), /^raidRunning: /);
  for (const raidDifficulty of [undefined, 'normal', 'Insane']) {
    assert.match(errorOf(heartbeat({ raidDifficulty })), /^raidDifficulty: /);
  }
  assert.equal(validateHeartbeat(heartbeat({ raidRunning: false, raidDifficulty: '' })).ok, true);
  for (const raidSecondsLeft of [undefined, -1, 86401, '5', NaN]) {
    assert.match(errorOf(heartbeat({ raidSecondsLeft })), /^raidSecondsLeft: /);
  }
  assert.equal(validateHeartbeat(heartbeat({ raidSecondsLeft: 811.7 })).value.raidSecondsLeft, 811);
  assert.equal(validateHeartbeat(heartbeat({ raidSecondsLeft: 86400 })).ok, true);
});

test('version rules', () => {
  for (const version of [undefined, '', 'v'.repeat(33), 1]) assert.match(errorOf(heartbeat({ version })), /^version: /);
  assert.equal(validateHeartbeat(heartbeat({ version: '1' })).ok, true);
});

test('normalizeAddress, isValidHost, clientAddress', () => {
  assert.equal(normalizeAddress('::ffff:127.0.0.1'), '127.0.0.1');
  assert.equal(normalizeAddress('::FFFF:10.0.0.1'), '10.0.0.1');
  assert.equal(normalizeAddress('::1'), '::1');
  assert.equal(normalizeAddress(undefined), '');
  assert.equal(isValidHost('localhost'), true);
  assert.equal(isValidHost('example.com.'), true);
  assert.equal(isValidHost(''), false);
  assert.equal(clientAddress('::ffff:10.0.0.5', '1.2.3.4, 5.6.7.8', false), '10.0.0.5');
  assert.equal(clientAddress('::ffff:10.0.0.5', ' 1.2.3.4 , 5.6.7.8', true), '1.2.3.4');
  assert.equal(clientAddress('::ffff:10.0.0.5', 'garbage', true), '10.0.0.5');
  assert.equal(clientAddress('::ffff:10.0.0.5', undefined, true), '10.0.0.5');
});
