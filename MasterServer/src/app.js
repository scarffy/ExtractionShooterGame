// Express app factory for the master server. All state lives in memory; see MASTER_API.md for the contract.

import { createHash, timingSafeEqual } from 'node:crypto';
import express from 'express';
import { createRegistry } from './registry.js';
import { createRateLimiter } from './rateLimit.js';
import { ID_PATTERN, clientAddress, validateHeartbeat } from './validate.js';

export const DEFAULT_LIMITS = {
  maxServers: 500,
  heartbeatPerIdWindowMs: 2000, // 1 heartbeat per 2 s per server id
  heartbeatPerIpPerMin: 60,
  listPerIpPerMin: 30,
  bodyLimit: '4kb',
};

const sha256 = (s) => createHash('sha256').update(s, 'utf8').digest();

/** Constant-time compare: both sides are hashed first, so buffers always have equal length. */
function tokenMatches(given, expected) {
  return timingSafeEqual(sha256(given), sha256(expected));
}

/**
 * @param {object} [opts]
 * @param {string} [opts.token]       shared secret for game servers; empty = no auth (dev only)
 * @param {number} [opts.ttl]         seconds a server stays listed after its last heartbeat (default 30)
 * @param {{ now(): number }} [opts.clock]
 * @param {Partial<typeof DEFAULT_LIMITS>} [opts.limits]
 * @param {boolean} [opts.trustProxy] use left-most X-Forwarded-For as the client address
 * @param {number} [opts.pruneIntervalMs] background pruning period (default 5000; 0 disables)
 * @param {(level: string, msg: string, fields?: object) => void} [opts.log]
 * @returns {{ app: import('express').Express, registry: ReturnType<typeof createRegistry>, close(): void }}
 */
export function createApp(opts = {}) {
  const token = opts.token ?? '';
  const ttlSeconds = opts.ttl ?? 30;
  const clock = opts.clock ?? { now: () => Date.now() };
  const limits = { ...DEFAULT_LIMITS, ...opts.limits };
  const trustProxy = Boolean(opts.trustProxy);
  const log = opts.log ?? (() => {});
  const startedAt = clock.now();

  const registry = createRegistry({
    ttlMs: ttlSeconds * 1000,
    maxEntries: limits.maxServers,
    clock,
    onExpire: (s) => log('info', 'server expired', { id: s.id, name: s.name }),
  });
  const perIdLimiter = createRateLimiter({ limit: 1, windowMs: limits.heartbeatPerIdWindowMs, clock });
  const heartbeatIpLimiter = createRateLimiter({ limit: limits.heartbeatPerIpPerMin, windowMs: 60_000, clock });
  const listIpLimiter = createRateLimiter({ limit: limits.listPerIpPerMin, windowMs: 60_000, clock });

  if (!token) log('warn', 'SERVER_TOKEN not set: heartbeats and deletes are accepted without a token (dev only)');

  const app = express();
  app.disable('x-powered-by');
  app.disable('etag');

  const send = (res, status, body) => res.status(status).json(body);
  const error = (res, status, message) => send(res, status, { ok: false, error: message });

  const remoteOf = (req) => clientAddress(req.socket.remoteAddress, req.get('x-forwarded-for'), trustProxy);

  const rateLimit = (limiter) => (req, res, next) => {
    const r = limiter.hit(remoteOf(req));
    if (r.allowed) return next();
    res.set('Retry-After', String(r.retryAfterSeconds));
    return error(res, 429, 'rate limited');
  };

  const requireToken = (req, res, next) => {
    if (!token) return next();
    const m = /^Bearer\s+(.+)$/i.exec(req.get('authorization') ?? '');
    if (m && tokenMatches(m[1].trim(), token)) return next();
    log('warn', 'unauthorized', { method: req.method, path: req.path, remote: remoteOf(req) });
    return error(res, 401, 'unauthorized');
  };

  // Parse JSON whatever the Content-Type says (UnityWebRequest headers vary); capped at 4 KB.
  const jsonBody = express.json({ limit: limits.bodyLimit, type: () => true });

  app.use((req, res, next) => {
    res.set('X-Content-Type-Options', 'nosniff');
    res.set('Cache-Control', 'no-store');
    if (req.method === 'GET' || req.method === 'HEAD') res.set('Access-Control-Allow-Origin', '*');
    next();
  });

  app.get('/health', (req, res) => {
    send(res, 200, {
      ok: true,
      servers: registry.size(),
      uptimeSeconds: Math.floor((clock.now() - startedAt) / 1000),
    });
  });

  app.post('/api/servers/heartbeat', rateLimit(heartbeatIpLimiter), requireToken, jsonBody, (req, res) => {
    const result = validateHeartbeat(req.body);
    if (!result.ok) {
      log('warn', 'invalid heartbeat', { error: result.error, remote: remoteOf(req) });
      return error(res, 400, result.error);
    }
    const server = result.value;

    const idLimit = perIdLimiter.hit(server.id);
    if (!idLimit.allowed) {
      res.set('Retry-After', String(idLimit.retryAfterSeconds));
      return error(res, 429, 'rate limited');
    }

    if (server.host === '') server.host = remoteOf(req);

    const stored = registry.upsert(server);
    if (!stored.ok) {
      log('warn', 'registry full', { id: server.id, max: limits.maxServers });
      return error(res, 503, 'full');
    }
    if (stored.created) {
      log('info', 'server registered', {
        id: server.id, name: server.name, host: server.host, port: server.port, version: server.version,
      });
    }
    return send(res, 200, { ok: true, ttlSeconds });
  });

  app.delete('/api/servers/:id', rateLimit(heartbeatIpLimiter), requireToken, (req, res) => {
    const { id } = req.params;
    if (!ID_PATTERN.test(id)) return error(res, 400, 'id: must be 8-64 chars of [A-Za-z0-9_-]');
    if (registry.remove(id)) log('info', 'server removed', { id });
    return send(res, 200, { ok: true });
  });

  app.get('/api/servers', rateLimit(listIpLimiter), (req, res) => {
    const filter = {};
    for (const key of ['version', 'region']) {
      const v = req.query[key];
      if (v === undefined || v === '') continue; // empty value = no filter
      if (typeof v !== 'string') return error(res, 400, `${key}: must be a single string`);
      filter[key] = v;
    }
    return send(res, 200, { servers: registry.list(filter) });
  });

  app.use((req, res) => error(res, 404, 'not found'));

  // Never leak stack traces: map body-parser errors, hide everything else behind "internal".
  // eslint-disable-next-line no-unused-vars
  app.use((err, req, res, next) => {
    if (err?.type === 'entity.too.large') return error(res, 413, 'body: too large');
    if (err?.type === 'entity.parse.failed') return error(res, 400, 'body: invalid JSON');
    const status = Number(err?.status ?? err?.statusCode);
    if (status >= 400 && status < 500) return error(res, status, 'bad request');
    log('error', 'unhandled error', { path: req.path, error: String(err?.message ?? err) });
    return error(res, 500, 'internal');
  });

  const pruneIntervalMs = opts.pruneIntervalMs ?? 5000;
  let timer = null;
  if (pruneIntervalMs > 0) {
    timer = setInterval(() => {
      registry.prune();
      perIdLimiter.prune();
      heartbeatIpLimiter.prune();
      listIpLimiter.prune();
    }, pruneIntervalMs);
    timer.unref();
  }

  return {
    app,
    registry,
    close() {
      if (timer) clearInterval(timer);
    },
  };
}
