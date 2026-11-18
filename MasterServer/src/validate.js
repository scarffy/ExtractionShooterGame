// Heartbeat validation and address helpers. Pure functions, no I/O.
// Rules follow MASTER_API.md exactly; the first failing field is reported as "<field>: <reason>".

import { isIP, isIPv4, isIPv6 } from 'node:net';

export const ID_PATTERN = /^[A-Za-z0-9_-]{8,64}$/;
const REGION_PATTERN = /^[a-z0-9-]{0,16}$/;
const HOSTNAME_LABEL = /^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$/;
// C0 controls, DEL and C1 controls.
const CONTROL_CHARS = /[\u0000-\u001F\u007F-\u009F]/;

const DIFFICULTIES = ['EasyNormal', 'NormalHard'];
const RAID_DIFFICULTIES = ['Easy', 'Normal', 'Hard', ''];

/** Length in Unicode code points (so an emoji counts as 1, not 2). */
function charLength(s) {
  return [...s].length;
}

function isInt(v) {
  return typeof v === 'number' && Number.isInteger(v);
}

/** "::ffff:1.2.3.4" -> "1.2.3.4"; anything else is returned unchanged. */
export function normalizeAddress(addr) {
  if (typeof addr !== 'string') return '';
  const m = /^::ffff:(\d{1,3}(?:\.\d{1,3}){3})$/i.exec(addr);
  return m && isIPv4(m[1]) ? m[1] : addr;
}

/** True for an IPv4/IPv6 literal or a syntactically valid DNS hostname (<= 253 chars). */
export function isValidHost(host) {
  if (typeof host !== 'string' || host.length === 0 || host.length > 253) return false;
  if (isIPv4(host) || isIPv6(host)) return true;
  const name = host.endsWith('.') ? host.slice(0, -1) : host;
  return name.length > 0 && name.split('.').every((label) => HOSTNAME_LABEL.test(label));
}

/**
 * Picks the address a heartbeat came from.
 * With trustProxy, the left-most X-Forwarded-For entry wins (only safe behind a proxy that
 * overwrites that header, e.g. Caddy's default); otherwise the TCP peer address is used.
 */
export function clientAddress(socketAddress, forwardedFor, trustProxy) {
  if (trustProxy && typeof forwardedFor === 'string' && forwardedFor.length > 0) {
    const first = forwardedFor.split(',')[0].trim();
    if (isIP(first)) return normalizeAddress(first);
  }
  return normalizeAddress(socketAddress ?? '');
}

function fail(field, reason) {
  return { ok: false, error: `${field}: ${reason}` };
}

/**
 * Validates a parsed heartbeat body.
 * Returns { ok: true, value } with only the known fields (host trimmed/normalized, may be ''),
 * or { ok: false, error: '<field>: <reason>' }.
 */
export function validateHeartbeat(body) {
  if (body === null || typeof body !== 'object' || Array.isArray(body)) {
    return fail('body', 'must be a JSON object');
  }
  const b = body;

  if (typeof b.id !== 'string' || !ID_PATTERN.test(b.id)) {
    return fail('id', 'must be 8-64 chars of [A-Za-z0-9_-]');
  }

  if (typeof b.name !== 'string') return fail('name', 'must be a string');
  const name = b.name.trim();
  if (charLength(name) < 1 || charLength(name) > 40) return fail('name', 'must be 1-40 chars after trimming');
  if (CONTROL_CHARS.test(name)) return fail('name', 'must not contain control characters');

  let host = '';
  if (b.host !== undefined && b.host !== null) {
    if (typeof b.host !== 'string') return fail('host', 'must be a string');
    host = b.host.trim();
    if (host !== '') {
      if (!isValidHost(host)) return fail('host', 'must be a hostname or IP address (max 253 chars)');
      host = normalizeAddress(host);
    }
  }

  if (!isInt(b.port) || b.port < 1 || b.port > 65535) return fail('port', 'must be an integer 1-65535');

  let region = '';
  if (b.region !== undefined && b.region !== null) {
    if (typeof b.region !== 'string' || !REGION_PATTERN.test(b.region)) {
      return fail('region', 'must be 0-16 chars of [a-z0-9-]');
    }
    region = b.region;
  }

  if (typeof b.map !== 'string' || charLength(b.map) < 1 || charLength(b.map) > 32) {
    return fail('map', 'must be 1-32 chars');
  }
  if (CONTROL_CHARS.test(b.map)) return fail('map', 'must not contain control characters');

  if (!DIFFICULTIES.includes(b.difficulties)) {
    return fail('difficulties', 'must be "EasyNormal" or "NormalHard"');
  }

  if (!isInt(b.maxPlayers) || b.maxPlayers < 1 || b.maxPlayers > 128) {
    return fail('maxPlayers', 'must be an integer 1-128');
  }
  if (!isInt(b.players) || b.players < 0 || b.players > b.maxPlayers) {
    return fail('players', 'must be an integer 0..maxPlayers');
  }

  if (typeof b.raidRunning !== 'boolean') return fail('raidRunning', 'must be a boolean');
  if (!RAID_DIFFICULTIES.includes(b.raidDifficulty)) {
    return fail('raidDifficulty', 'must be "Easy", "Normal", "Hard" or ""');
  }
  // Accept any number in range (Unity may send a float) and store whole seconds.
  if (typeof b.raidSecondsLeft !== 'number' || !Number.isFinite(b.raidSecondsLeft)
    || b.raidSecondsLeft < 0 || b.raidSecondsLeft > 86400) {
    return fail('raidSecondsLeft', 'must be a number 0-86400');
  }

  if (typeof b.version !== 'string' || charLength(b.version) < 1 || charLength(b.version) > 32) {
    return fail('version', 'must be 1-32 chars');
  }
  if (CONTROL_CHARS.test(b.version)) return fail('version', 'must not contain control characters');

  return {
    ok: true,
    value: {
      id: b.id,
      name,
      host,
      port: b.port,
      region,
      map: b.map,
      difficulties: b.difficulties,
      players: b.players,
      maxPlayers: b.maxPlayers,
      raidRunning: b.raidRunning,
      raidDifficulty: b.raidDifficulty,
      raidSecondsLeft: Math.floor(b.raidSecondsLeft),
      version: b.version,
    },
  };
}
