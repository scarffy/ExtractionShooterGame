# Master server API (contract between the Unity game and the Node master server)

The master server is a small public HTTP service that keeps the list of running dedicated game servers.
Game servers announce themselves; game clients read the list. It never relays game traffic (that stays UDP
between client and game server via FishNet/Tugboat).

Base URL example: `https://master.example.com` (dev: `http://localhost:8080`). All bodies are JSON (UTF-8).

## Endpoints

### `GET /health`
`200 {"ok":true,"servers":<count>,"uptimeSeconds":<n>}`

### `POST /api/servers/heartbeat`  (game server → master, every 10 s)
Header: `Authorization: Bearer <SERVER_TOKEN>` (shared secret set on the master with env `SERVER_TOKEN`;
if the master has no token configured, heartbeats are accepted without one — dev only, logged as a warning).

Body:
```json
{
  "id": "6f1c2a9e0b5d4c3a8e7f6a5b4c3d2e1f",
  "name": "Kim's server #1",
  "host": "",
  "port": 7770,
  "region": "sg",
  "map": "Sandbox",
  "difficulties": "EasyNormal",
  "players": 3,
  "maxPlayers": 12,
  "raidRunning": true,
  "raidDifficulty": "Normal",
  "raidSecondsLeft": 812,
  "version": "0.5.0"
}
```
Rules (master validates; reject with `400 {"ok":false,"error":"<field>: <reason>"}`):
- `id`: 8–64 chars `[A-Za-z0-9_-]`. Stable for the life of the server process (new GUID each start is fine).
- `name`: 1–40 chars after trimming, no control characters.
- `host`: optional, ≤ 253 chars hostname or IPv4/IPv6 literal. Empty → the master uses the request's remote address
  (with env `TRUST_PROXY=1`, the left-most `X-Forwarded-For`). IPv4-mapped IPv6 (`::ffff:1.2.3.4`) is shown as `1.2.3.4`.
- `port`: 1–65535 (UDP game port).
- `region`: 0–16 chars `[a-z0-9-]` (e.g. `sg`, `my-kl`), may be empty.
- `map`: 1–32 chars. `difficulties`: `"EasyNormal"` or `"NormalHard"`.
- `players`: 0..maxPlayers. `maxPlayers`: 1–128.
- `raidRunning`: bool. `raidDifficulty`: `"Easy"|"Normal"|"Hard"|""`. `raidSecondsLeft`: 0–86400.
- `version`: 1–32 chars (the game build version; clients only join matching versions).
- Body ≤ 4 KB. Unknown fields are ignored.

Response: `200 {"ok":true,"ttlSeconds":30}`. Wrong/missing token: `401 {"ok":false,"error":"unauthorized"}`.
An `id` already registered from a different source address+port pair is still updated only when the token matches
(token is required in production). Max 500 servers; when full, new ids get `503 {"ok":false,"error":"full"}`.

A server not heard from for `ttlSeconds` (30) disappears from the list.

### `DELETE /api/servers/:id`  (game server → master, on shutdown)
Same `Authorization` header. `200 {"ok":true}` (also when unknown).

### `GET /api/servers`  (game client → master)
Optional query `version=0.5.0` (only that version), `region=sg`.
Response `200`:
```json
{
  "servers": [
    {
      "id": "6f1c…", "name": "Kim's server #1", "host": "203.0.113.10", "port": 7770, "region": "sg",
      "map": "Sandbox", "difficulties": "EasyNormal", "players": 3, "maxPlayers": 12,
      "raidRunning": true, "raidDifficulty": "Normal", "raidSecondsLeft": 812,
      "version": "0.5.0", "lastSeenSecondsAgo": 4
    }
  ]
}
```
Sorted by players descending, then name. `Cache-Control: no-store`. CORS: `Access-Control-Allow-Origin: *` on GET.

## Rate limits (master)
- Heartbeat: ≤ 1 per 2 s per server id (extra → `429`), ≤ 60/min per source IP.
- List: ≤ 30/min per source IP.

## Unity side notes
- Unity's `JsonUtility` maps the list response with `[Serializable] class ServerListResponse { public ServerEntry[] servers; }`
  and the heartbeat with a plain serializable class — so keep field names exactly as above (camelCase).
- Version string: `Application.version` (Player Settings → Version).
