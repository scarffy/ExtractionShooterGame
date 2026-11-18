// End-to-end HTTP tests: the real app on an ephemeral port, driven with global fetch.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createApp } from '../src/app.js';
import { fakeClock, heartbeat } from './helpers.js';

const TOKEN = 'test-secret-token';

/** Starts an app; returns helpers bound to it. Closed automatically when the test ends. */
async function start(t, opts = {}, listenHost = '127.0.0.1') {
  const clock = fakeClock();
  const logs = [];
  const master = createApp({
    token: TOKEN,
    clock,
    pruneIntervalMs: 0,
    log: (level, msg, fields) => logs.push({ level, msg, ...fields }),
    ...opts,
  });
  const server = await new Promise((resolve, reject) => {
    const s = master.app.listen(0, listenHost, () => resolve(s));
    s.on('error', reject);
  });
  t.after(() => {
    master.close();
    server.closeAllConnections();
    return new Promise((r) => server.close(r));
  });
  const base = `http://127.0.0.1:${server.address().port}`;

  const request = async (method, path, { body, token = TOKEN, headers = {} } = {}) => {
    const h = { ...headers };
    if (token) h.authorization = `Bearer ${token}`;
    if (body !== undefined && typeof body !== 'string') h['content-type'] = 'application/json';
    const res = await fetch(base + path, {
      method,
      headers: h,
      body: body === undefined ? undefined : typeof body === 'string' ? body : JSON.stringify(body),
    });
    const text = await res.text();
    return { status: res.status, headers: res.headers, json: text ? JSON.parse(text) : undefined, text };
  };

  return {
    clock,
    logs,
    master,
    request,
    beat: (body, o) => request('POST', '/api/servers/heartbeat', { body, ...o }),
    list: (query = '') => request('GET', `/api/servers${query}`),
  };
}

test('GET /health', async (t) => {
  const m = await start(t);
  m.clock.advance(12_345);
  const r = await m.request('GET', '/health');
  assert.equal(r.status, 200);
  assert.deepEqual(r.json, { ok: true, servers: 0, uptimeSeconds: 12 });
  assert.equal(r.headers.get('x-content-type-options'), 'nosniff');
});

test('valid heartbeat appears in the list with the contract shape', async (t) => {
  const m = await start(t);
  const hb = heartbeat({ id: '6f1c2a9e0b5d4c3a8e7f6a5b4c3d2e1f', name: "Kim's server #1", host: '203.0.113.10' });
  const r = await m.beat({ ...hb, unknownField: 1 });
  assert.equal(r.status, 200);
  assert.deepEqual(r.json, { ok: true, ttlSeconds: 30 });

  m.clock.advance(4_000);
  const l = await m.list();
  assert.equal(l.status, 200);
  assert.equal(l.headers.get('cache-control'), 'no-store');
  assert.equal(l.headers.get('access-control-allow-origin'), '*');
  assert.deepEqual(l.json, {
    servers: [{
      id: '6f1c2a9e0b5d4c3a8e7f6a5b4c3d2e1f', name: "Kim's server #1", host: '203.0.113.10', port: 7770,
      region: 'sg', map: 'Sandbox', difficulties: 'EasyNormal', players: 3, maxPlayers: 12,
      raidRunning: true, raidDifficulty: 'Normal', raidSecondsLeft: 812, version: '0.5.0', lastSeenSecondsAgo: 4,
    }],
  });
  assert.equal((await m.request('GET', '/health')).json.servers, 1);
  assert.ok(m.logs.some((l) => l.msg === 'server registered'));
});

test('TTL expiry with fake clock (and custom ttl is reported)', async (t) => {
  const m = await start(t, { ttl: 20 });
  const r = await m.beat(heartbeat());
  assert.equal(r.json.ttlSeconds, 20);
  m.clock.advance(19_999);
  assert.equal((await m.list()).json.servers.length, 1);
  m.clock.advance(1);
  assert.equal((await m.list()).json.servers.length, 0);
  assert.ok(m.logs.some((l) => l.msg === 'server expired'));
});

test('unauthorized: missing, wrong, or malformed token', async (t) => {
  const m = await start(t);
  for (const opts of [{ token: '' }, { token: 'wrong' }, { token: '', headers: { authorization: TOKEN } }]) {
    const r = await m.beat(heartbeat(), opts);
    assert.equal(r.status, 401);
    assert.deepEqual(r.json, { ok: false, error: 'unauthorized' });
  }
  const d = await m.request('DELETE', '/api/servers/abcdefgh', { token: 'wrong' });
  assert.equal(d.status, 401);
  assert.equal((await m.list()).json.servers.length, 0);
  assert.ok(m.logs.every((l) => !JSON.stringify(l).includes(TOKEN)), 'token must never be logged');
});

test('no token configured: accepted without auth, warning logged', async (t) => {
  const m = await start(t, { token: '' });
  assert.ok(m.logs.some((l) => l.level === 'warn' && /SERVER_TOKEN/.test(l.msg)));
  assert.equal((await m.beat(heartbeat(), { token: '' })).status, 200);
  assert.equal((await m.beat(heartbeat(), { token: 'anything' })).status, 200);
});

test('validation errors are reported per field', async (t) => {
  const m = await start(t);
  const cases = {
    id: { id: 'bad' },
    name: { name: '   ' },
    host: { host: 'not a host' },
    port: { port: 0 },
    region: { region: 'SG' },
    map: { map: '' },
    difficulties: { difficulties: 'Easy' },
    maxPlayers: { maxPlayers: 129 },
    players: { players: 13 },
    raidRunning: { raidRunning: 'yes' },
    raidDifficulty: { raidDifficulty: 'Insane' },
    raidSecondsLeft: { raidSecondsLeft: 90000 },
    version: { version: '' },
  };
  for (const [field, override] of Object.entries(cases)) {
    const r = await m.beat(heartbeat(override));
    assert.equal(r.status, 400, field);
    assert.equal(r.json.ok, false);
    assert.ok(r.json.error.startsWith(`${field}: `), `${field} -> ${r.json.error}`);
  }
  const notJson = await m.beat('{"id": oops');
  assert.deepEqual([notJson.status, notJson.json], [400, { ok: false, error: 'body: invalid JSON' }]);
  const array = await m.beat([heartbeat()]);
  assert.deepEqual([array.status, array.json.error], [400, 'body: must be a JSON object']);
  const empty = await m.request('POST', '/api/servers/heartbeat');
  assert.deepEqual([empty.status, empty.json.error.split(':')[0]], [400, 'id']); // no body = all fields missing
  assert.ok(!notJson.text.includes(' at '), 'no stack traces');
});

test('JSON is parsed even without a JSON content type', async (t) => {
  const m = await start(t);
  const r = await m.beat(JSON.stringify(heartbeat()), { headers: { 'content-type': 'text/plain' } });
  assert.equal(r.status, 200);
});

test('empty host falls back to the remote address', async (t) => {
  const m = await start(t);
  await m.beat(heartbeat({ host: '' }));
  assert.equal((await m.list()).json.servers[0].host, '127.0.0.1');
});

test('X-Forwarded-For is ignored by default and used with trustProxy', async (t) => {
  const plain = await start(t);
  await plain.beat(heartbeat(), { headers: { 'x-forwarded-for': '198.51.100.7' } });
  assert.equal((await plain.list()).json.servers[0].host, '127.0.0.1');

  const proxied = await start(t, { trustProxy: true });
  await proxied.beat(heartbeat(), { headers: { 'x-forwarded-for': '198.51.100.7, 10.0.0.1' } });
  await proxied.beat(heartbeat({ players: 0 }), { headers: { 'x-forwarded-for': '::ffff:198.51.100.8' } });
  assert.deepEqual((await proxied.list()).json.servers.map((s) => s.host), ['198.51.100.7', '198.51.100.8']);
});

test('IPv4-mapped IPv6 remote address is shown as IPv4', async (t) => {
  let m;
  try {
    m = await start(t, {}, '::'); // dual-stack socket: IPv4 peers appear as ::ffff:127.0.0.1
  } catch {
    t.skip('IPv6 not available');
    return;
  }
  await m.beat(heartbeat());
  assert.equal((await m.list()).json.servers[0].host, '127.0.0.1');
});

test('DELETE removes a server and is 200 for unknown ids', async (t) => {
  const m = await start(t);
  const hb = heartbeat();
  await m.beat(hb);
  const d = await m.request('DELETE', `/api/servers/${hb.id}`);
  assert.deepEqual([d.status, d.json], [200, { ok: true }]);
  assert.equal((await m.list()).json.servers.length, 0);
  const again = await m.request('DELETE', `/api/servers/${hb.id}`);
  assert.deepEqual([again.status, again.json], [200, { ok: true }]);
  assert.equal((await m.request('DELETE', '/api/servers/bad!')).status, 400);
});

test('version and region filters', async (t) => {
  const m = await start(t);
  await m.beat(heartbeat({ name: 'a', version: '0.5.0', region: 'sg' }));
  await m.beat(heartbeat({ name: 'b', version: '0.5.0', region: 'my-kl' }));
  await m.beat(heartbeat({ name: 'c', version: '0.4.0', region: 'sg' }));
  const names = async (q) => (await m.list(q)).json.servers.map((s) => s.name).sort();
  assert.deepEqual(await names('?version=0.5.0'), ['a', 'b']);
  assert.deepEqual(await names('?region=sg'), ['a', 'c']);
  assert.deepEqual(await names('?version=0.5.0&region=my-kl'), ['b']);
  assert.deepEqual(await names('?version=9.9.9'), []);
  assert.deepEqual(await names('?version=&region='), ['a', 'b', 'c']);
  assert.equal((await m.list('?version=a&version=b')).status, 400);
});

test('sorted by players descending, then name', async (t) => {
  const m = await start(t);
  await m.beat(heartbeat({ name: 'Zulu', players: 1 }));
  await m.beat(heartbeat({ name: 'bravo', players: 8 }));
  await m.beat(heartbeat({ name: 'Alpha', players: 8 }));
  await m.beat(heartbeat({ name: 'Mike', players: 0 }));
  assert.deepEqual((await m.list()).json.servers.map((s) => s.name), ['Alpha', 'bravo', 'Zulu', 'Mike']);
});

test('rate limit: 1 heartbeat per 2 s per server id', async (t) => {
  const m = await start(t);
  const hb = heartbeat();
  assert.equal((await m.beat(hb)).status, 200);
  const r = await m.beat({ ...hb, players: 4 });
  assert.deepEqual([r.status, r.json], [429, { ok: false, error: 'rate limited' }]);
  assert.equal(r.headers.get('retry-after'), '2');
  m.clock.advance(2_000);
  assert.equal((await m.beat({ ...hb, players: 4 })).status, 200);
  assert.equal((await m.list()).json.servers[0].players, 4);
});

test('rate limit: 60 heartbeats per minute per IP', async (t) => {
  const m = await start(t);
  for (let i = 0; i < 60; i++) assert.equal((await m.beat(heartbeat())).status, 200);
  assert.equal((await m.beat(heartbeat())).status, 429);
  m.clock.advance(60_000);
  assert.equal((await m.beat(heartbeat())).status, 200);
});

test('rate limit: 30 list requests per minute per IP', async (t) => {
  const m = await start(t);
  for (let i = 0; i < 30; i++) assert.equal((await m.list()).status, 200);
  const r = await m.list();
  assert.equal(r.status, 429);
  assert.equal(r.headers.get('access-control-allow-origin'), '*');
  m.clock.advance(60_000);
  assert.equal((await m.list()).status, 200);
});

test('full registry returns 503 for new ids only', async (t) => {
  const m = await start(t, { limits: { maxServers: 2 } });
  const a = heartbeat();
  await m.beat(a);
  await m.beat(heartbeat());
  const r = await m.beat(heartbeat());
  assert.deepEqual([r.status, r.json], [503, { ok: false, error: 'full' }]);
  m.clock.advance(2_000);
  assert.equal((await m.beat(a)).status, 200, 'existing id still refreshes');
});

test('oversized body returns 413', async (t) => {
  const m = await start(t);
  const r = await m.beat(heartbeat({ padding: 'x'.repeat(5000) }));
  assert.deepEqual([r.status, r.json], [413, { ok: false, error: 'body: too large' }]);
  assert.equal((await m.beat(heartbeat({ padding: 'x'.repeat(3500) }))).status, 200, 'under 4 KB is fine');
});

test('unknown routes return JSON 404', async (t) => {
  const m = await start(t);
  const r = await m.request('GET', '/nope');
  assert.deepEqual([r.status, r.json], [404, { ok: false, error: 'not found' }]);
});
