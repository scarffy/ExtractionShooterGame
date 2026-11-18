// Entry point: reads env, starts HTTP, shuts down cleanly on SIGTERM/SIGINT.

import { createApp, DEFAULT_LIMITS } from './app.js';

/** One JSON object per line, e.g. {"time":"...","level":"info","msg":"listening","port":8080} */
function log(level, msg, fields = {}) {
  const line = JSON.stringify({ time: new Date().toISOString(), level, msg, ...fields });
  if (level === 'error') console.error(line);
  else console.log(line);
}

function intEnv(name, fallback, min, max) {
  const raw = process.env[name];
  if (raw === undefined || raw.trim() === '') return fallback;
  const n = Number(raw);
  if (!Number.isInteger(n) || n < min || n > max) {
    log('error', 'invalid environment variable', { name, expected: `integer ${min}-${max}` });
    process.exit(1);
  }
  return n;
}

const port = intEnv('PORT', 8080, 1, 65535);
const host = process.env.HOST || undefined; // unset = all interfaces
const token = process.env.SERVER_TOKEN ?? '';
const trustProxy = ['1', 'true', 'yes'].includes((process.env.TRUST_PROXY ?? '').toLowerCase());
const ttl = intEnv('TTL_SECONDS', 30, 5, 3600);
const limits = {
  maxServers: intEnv('MAX_SERVERS', DEFAULT_LIMITS.maxServers, 1, 100000),
  heartbeatPerIpPerMin: intEnv('RATE_HEARTBEAT_PER_MIN', DEFAULT_LIMITS.heartbeatPerIpPerMin, 1, 100000),
  listPerIpPerMin: intEnv('RATE_LIST_PER_MIN', DEFAULT_LIMITS.listPerIpPerMin, 1, 100000),
};

const master = createApp({ token, ttl, trustProxy, limits, log });

const server = master.app.listen(port, host, () => {
  log('info', 'listening', {
    port, host: host ?? 'all', ttlSeconds: ttl, trustProxy, auth: token ? 'token' : 'none', ...limits,
  });
});
server.on('error', (err) => {
  log('error', 'server error', { error: err.message });
  process.exit(1);
});
// Longer than typical reverse-proxy idle timeouts, so Caddy never reuses a socket Node just closed.
server.keepAliveTimeout = 65_000;
server.headersTimeout = 66_000;
server.requestTimeout = 15_000;

let stopping = false;
function shutdown(signal) {
  if (stopping) return;
  stopping = true;
  log('info', 'shutting down', { signal });
  master.close();
  server.close(() => {
    log('info', 'stopped');
    process.exit(0);
  });
  server.closeIdleConnections();
  setTimeout(() => {
    log('warn', 'forcing exit after timeout');
    process.exit(1);
  }, 5000).unref();
}
process.on('SIGTERM', () => shutdown('SIGTERM'));
process.on('SIGINT', () => shutdown('SIGINT'));
