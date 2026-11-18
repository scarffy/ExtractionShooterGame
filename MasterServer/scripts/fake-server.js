#!/usr/bin/env node
// Pretends to be one or more dedicated game servers: sends a heartbeat every 10 s and DELETEs on Ctrl+C.
// Lets you test the Unity server browser without running a real dedicated server.
//
//   node scripts/fake-server.js --name "Kim's test" --port 7770 --players 3
//   node scripts/fake-server.js --count 5 --version 0.5.0 --master https://master.example.com --token <secret>
//
// Token defaults to the SERVER_TOKEN env var. No game port is opened; joining these servers will fail.

import { randomUUID } from 'node:crypto';
import { parseArgs } from 'node:util';

const { values: a } = parseArgs({
  options: {
    master: { type: 'string', default: process.env.MASTER_URL ?? 'http://localhost:8080' },
    token: { type: 'string', default: process.env.SERVER_TOKEN ?? '' },
    name: { type: 'string', default: 'Fake server' },
    host: { type: 'string', default: '' },
    port: { type: 'string', default: '7770' },
    players: { type: 'string', default: '0' },
    'max-players': { type: 'string', default: '12' },
    region: { type: 'string', default: '' },
    map: { type: 'string', default: 'Sandbox' },
    difficulties: { type: 'string', default: 'EasyNormal' },
    version: { type: 'string', default: '0.5.0' },
    raid: { type: 'boolean', default: false },
    count: { type: 'string', default: '1' },
    interval: { type: 'string', default: '10' },
    help: { type: 'boolean', short: 'h', default: false },
  },
});

if (a.help) {
  console.log(`Usage: node scripts/fake-server.js [options]
  --master <url>        master base URL (default http://localhost:8080, or env MASTER_URL)
  --token <secret>      shared token (default env SERVER_TOKEN)
  --name <text>         server name (with --count > 1, " #n" is appended)
  --host <addr>         advertised host (default empty = master uses your address)
  --port <n>            game port (default 7770; +1 per extra fake server)
  --players <n>         current players (default 0)
  --max-players <n>     max players (default 12)
  --region <code>       e.g. sg, my-kl (default empty)
  --map <name>          default Sandbox
  --difficulties <d>    EasyNormal | NormalHard (default EasyNormal)
  --version <v>         must match the game's Application.version (default 0.5.0)
  --raid                report a running raid with a countdown
  --count <n>           number of fake servers to run (default 1)
  --interval <s>        seconds between heartbeats (default 10)`);
  process.exit(0);
}

const base = a.master.replace(/\/+$/, '');
const headers = { 'content-type': 'application/json' };
if (a.token) headers.authorization = `Bearer ${a.token}`;

const count = Number(a.count);
const raidLength = 1800;
const startedAt = Date.now();

const servers = Array.from({ length: count }, (_, i) => ({
  id: randomUUID().replaceAll('-', ''),
  name: count > 1 ? `${a.name} #${i + 1}` : a.name,
  host: a.host,
  port: Number(a.port) + i,
  region: a.region,
  map: a.map,
  difficulties: a.difficulties,
  players: Number(a.players),
  maxPlayers: Number(a['max-players']),
  version: a.version,
}));

async function beat(s) {
  const elapsed = Math.floor((Date.now() - startedAt) / 1000);
  const body = {
    ...s,
    raidRunning: a.raid,
    raidDifficulty: a.raid ? (a.difficulties === 'NormalHard' ? 'Hard' : 'Normal') : '',
    raidSecondsLeft: a.raid ? Math.max(0, raidLength - elapsed) : 0,
  };
  try {
    const res = await fetch(`${base}/api/servers/heartbeat`, { method: 'POST', headers, body: JSON.stringify(body) });
    const text = await res.text();
    console.log(`${new Date().toISOString()} ${s.name} -> ${res.status} ${text}`);
  } catch (err) {
    console.log(`${new Date().toISOString()} ${s.name} -> network error: ${err.cause?.code ?? err.message}`);
  }
}

const tick = () => Promise.all(servers.map(beat));
await tick();
const timer = setInterval(tick, Number(a.interval) * 1000);

let stopping = false;
async function stop() {
  if (stopping) return;
  stopping = true;
  clearInterval(timer);
  console.log('\nRemoving fake servers...');
  await Promise.all(servers.map(async (s) => {
    try {
      const res = await fetch(`${base}/api/servers/${s.id}`, { method: 'DELETE', headers });
      console.log(`DELETE ${s.name} -> ${res.status}`);
    } catch (err) {
      console.log(`DELETE ${s.name} -> network error: ${err.cause?.code ?? err.message}`);
    }
  }));
  process.exit(0);
}
process.on('SIGINT', stop);
process.on('SIGTERM', stop);
