// Shared test helpers (not a test file itself).

export function fakeClock(start = 1_700_000_000_000) {
  let t = start;
  return {
    now: () => t,
    advance(ms) {
      t += ms;
    },
  };
}

let counter = 0;

/** A valid heartbeat body; override any field. Each call gets a fresh id unless one is given. */
export function heartbeat(overrides = {}) {
  counter += 1;
  return {
    id: `test-server-${String(counter).padStart(4, '0')}`,
    name: `Server ${counter}`,
    host: '',
    port: 7770,
    region: 'sg',
    map: 'Sandbox',
    difficulties: 'EasyNormal',
    players: 3,
    maxPlayers: 12,
    raidRunning: true,
    raidDifficulty: 'Normal',
    raidSecondsLeft: 812,
    version: '0.5.0',
    ...overrides,
  };
}
