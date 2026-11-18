# Master server (server list)

A small Node.js/Express service that keeps the list of running dedicated game servers for the extraction shooter.

- **Game servers** send a heartbeat every 10 s (`POST /api/servers/heartbeat`) and a `DELETE` on shutdown.
- **Game clients** read the list (`GET /api/servers`) for the server browser, then connect to the game server
  directly over UDP (FishNet/Tugboat). The master never relays game traffic.
- Everything is in memory. If the master restarts, the list refills within one heartbeat (10 s).
- A server not heard from for 30 s (`TTL_SECONDS`) drops off the list.

The API contract shared with the Unity side is in [`MASTER_API.md`](MASTER_API.md). Don't rename fields: Unity's `JsonUtility` maps them by name.

```
MasterServer/
  src/app.js          Express app factory (routes, auth, rate limits, error handling)
  src/registry.js     in-memory server list with TTL + size cap
  src/validate.js     heartbeat validation + client address helpers
  src/rateLimit.js    fixed-window rate limiter
  src/server.js       entry point: env vars, logging, graceful shutdown
  scripts/fake-server.js   pretend game server(s) for testing the Unity browser
  test/               node:test unit + HTTP tests
  Dockerfile, docker-compose.yml, Caddyfile, .env.example
```

Needs Node 20 or newer (22 recommended). Express is the only runtime dependency.

## Run locally

```bash
cd MasterServer
npm install
npm start                          # http://localhost:8080, no token (dev mode, logs a warning)
```

With a token, like production (bash / Git Bash):

```bash
SERVER_TOKEN=devtoken npm start
```

PowerShell: `$env:SERVER_TOKEN="devtoken"; npm start`

Logs are one JSON object per line, e.g.
`{"time":"...","level":"info","msg":"server registered","id":"6f1c...","name":"Kim's server #1","host":"127.0.0.1","port":7770,"version":"0.5.0"}`.
The token is never logged.

### Try every endpoint with curl

Run these in bash or Git Bash. In PowerShell, call `curl.exe` and put the JSON in a file (`-d "@hb.json"`).

```bash
# Health
curl http://localhost:8080/health
# {"ok":true,"servers":0,"uptimeSeconds":12}

# Heartbeat (what a game server sends every 10 s). Empty "host" = the master uses your IP address.
curl -X POST http://localhost:8080/api/servers/heartbeat \
  -H "Authorization: Bearer devtoken" -H "Content-Type: application/json" \
  -d '{"id":"6f1c2a9e0b5d4c3a8e7f6a5b4c3d2e1f","name":"Kims server #1","host":"","port":7770,"region":"sg","map":"Sandbox","difficulties":"EasyNormal","players":3,"maxPlayers":12,"raidRunning":true,"raidDifficulty":"Normal","raidSecondsLeft":812,"version":"0.5.0"}'
# {"ok":true,"ttlSeconds":30}

# List (what the game client calls). Both filters are optional.
curl "http://localhost:8080/api/servers"
curl "http://localhost:8080/api/servers?version=0.5.0&region=sg"
# {"servers":[{"id":"6f1c...","name":"Kims server #1","host":"127.0.0.1","port":7770,...,"lastSeenSecondsAgo":4}]}

# Remove (what a game server sends on shutdown). 200 even if the id is unknown.
curl -X DELETE http://localhost:8080/api/servers/6f1c2a9e0b5d4c3a8e7f6a5b4c3d2e1f \
  -H "Authorization: Bearer devtoken"
# {"ok":true}
```

Errors are always JSON, `{"ok":false,"error":"..."}`:

| Status | When |
|---|---|
| 400 | Invalid field, e.g. `"port: must be an integer 1-65535"`, or invalid JSON |
| 401 | Missing or wrong token (`"unauthorized"`) |
| 413 | Body over 4 KB |
| 429 | Rate limited (`Retry-After` header says how many seconds to wait) |
| 503 | 500 servers already listed and this is a new id (`"full"`) |

### Fake game servers (test the Unity server browser without a dedicated server)

```bash
# One server, heartbeat every 10 s; Ctrl+C sends DELETE so it disappears at once
SERVER_TOKEN=devtoken node scripts/fake-server.js --name "Kim test" --port 7770 --players 3

# Five servers with a running raid, against the VPS
node scripts/fake-server.js --count 5 --raid --region sg \
  --master https://master.example.com --token <secret>

node scripts/fake-server.js --help   # all options
```

`--version` must match the game's **Player Settings > Version** (`Application.version`), because the client only
shows servers with its own version. The default is `0.5.0`. Fake servers do not open a game port, so joining one fails.

## Environment variables

| Variable | Default | Meaning |
|---|---|---|
| `PORT` | `8080` | HTTP port |
| `HOST` | all interfaces | Bind address. Use `127.0.0.1` when Caddy runs on the same machine without Docker |
| `SERVER_TOKEN` | empty | Shared secret game servers send as `Authorization: Bearer <token>`. **Empty = no auth, dev only** |
| `TRUST_PROXY` | `0` | `1` = take the client IP from the left-most `X-Forwarded-For`. Only set this behind a reverse proxy that overwrites the header (Caddy does by default); otherwise anyone can fake their address |
| `TTL_SECONDS` | `30` | Seconds a server stays listed after its last heartbeat |
| `MAX_SERVERS` | `500` | Cap on listed servers |
| `RATE_HEARTBEAT_PER_MIN` | `60` | Heartbeats and deletes per source IP per minute |
| `RATE_LIST_PER_MIN` | `30` | List requests per source IP per minute |

There is also a fixed limit of one heartbeat per 2 s per server id.

Generate a token with `openssl rand -hex 32`.

## Tests

```bash
npm test
```

This uses the built-in `node:test` runner with no extra packages. There are unit tests for validation, the registry and the rate limiter, plus HTTP tests that
start the app on a random port with a fake clock (TTL expiry, auth, every validation rule, filters, sorting, 429,
503, 413...). The IPv4-mapped IPv6 HTTP test is skipped on machines without IPv6. The mapping itself is also unit-tested.

## Deploy on a small Linux VPS

Any 1 vCPU / 512 MB VPS is plenty. You need:

1. **A domain name.** Create a DNS **A record**, e.g. `master.yourdomain.com` → the VPS public IPv4 address.
   Wait until `ping master.yourdomain.com` shows the VPS IP.
2. **Firewall:** open **TCP 80 and 443** (and 22 for SSH). Port 80 is needed for the Let's Encrypt certificate.
   ```bash
   sudo ufw allow 22/tcp && sudo ufw allow 80/tcp && sudo ufw allow 443/tcp && sudo ufw enable
   ```
   Also open them in your provider's cloud firewall or security group if it has one.

### Option A: Docker Compose + Caddy (recommended)

Caddy terminates HTTPS and gets and renews the certificate automatically. The master is not exposed directly.

```bash
# On the VPS (Docker installed: https://docs.docker.com/engine/install/)
git clone <your repo> es && cd es/MasterServer     # or copy just the MasterServer folder
cp .env.example .env
nano .env        # set SERVER_TOKEN=<openssl rand -hex 32> and MASTER_DOMAIN=master.yourdomain.com
docker compose up -d --build

docker compose ps                      # master should become "healthy"
docker compose logs -f master          # JSON logs
curl https://master.yourdomain.com/health
```

To update after a `git pull`, run `docker compose up -d --build`. The certificates live in the `caddy_data` volume, so don't delete it.

### Option B: systemd, no Docker

```bash
# Node 22 (NodeSource) and Caddy (official apt repo: https://caddyserver.com/docs/install#debian-ubuntu-raspbian)
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash -
sudo apt install -y nodejs caddy

# from the MasterServer folder (git clone or scp):
sudo useradd --system --home /opt/es-master --shell /usr/sbin/nologin esmaster
sudo mkdir -p /opt/es-master && sudo cp -r src package.json package-lock.json /opt/es-master/
cd /opt/es-master && sudo npm ci --omit=dev

sudo tee /etc/es-master.env > /dev/null <<'EOF'
SERVER_TOKEN=<openssl rand -hex 32>
HOST=127.0.0.1
PORT=8080
TRUST_PROXY=1
EOF
sudo chmod 600 /etc/es-master.env
```

`/etc/systemd/system/es-master.service`:

```ini
[Unit]
Description=Extraction shooter master server
After=network-online.target
Wants=network-online.target

[Service]
User=esmaster
WorkingDirectory=/opt/es-master
EnvironmentFile=/etc/es-master.env
ExecStart=/usr/bin/node src/server.js
Restart=always
RestartSec=2
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true

[Install]
WantedBy=multi-user.target
```

`/etc/caddy/Caddyfile`:

```
master.yourdomain.com {
	encode gzip
	reverse_proxy 127.0.0.1:8080
}
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now es-master
sudo systemctl reload caddy
journalctl -u es-master -f
curl https://master.yourdomain.com/health
```

## Pointing game servers and clients at it

- **Master URL:** `https://master.yourdomain.com` (dev: `http://localhost:8080`). Clients only need this.
- **Token:** dedicated game servers need the same `SERVER_TOKEN`. Keep it on the server builds and out of client builds.
  Clients never send it.
- **`host` field:** leave it empty and the master lists the address the heartbeat came from. That's right when the
  game server sends heartbeats from the same public IP that players connect to. Set it explicitly (public IP or DNS name)
  when it doesn't. For example, the game server is behind NAT, uses a different outgoing IP, runs on the same VPS as the
  master, or the list shows a private address such as `172.x.x.x` or `10.x.x.x`.
- **Game port:** players connect straight to `host:port` over **UDP** (Tugboat, default 7770). Open that UDP port on the
  game server machine's firewall. The master itself only needs TCP 80/443.
- **Version:** the game sends `Application.version`, and clients filter with `?version=`, so bump **Player Settings >
  Version** whenever a build isn't network-compatible with the previous one.

## Troubleshooting

- **Server never shows up:** check the game server log for the heartbeat response. `401` means a wrong token, and `400` names the
  bad field. Make sure the client and the server use the same version string.
- **List shows the wrong host:** see the `host` notes above. Behind a proxy you also need `TRUST_PROXY=1`, which compose
  already sets.
- **429 on the list:** players behind one shared public IP (LAN party, internet café, mobile carrier NAT) share the
  30/min list limit. Raise `RATE_LIST_PER_MIN` if that's a problem.
- **429 on heartbeats from one machine running many servers:** each server sends 6 heartbeats a minute, so the default
  60/min per IP fits about 10 servers on one machine. Raise `RATE_HEARTBEAT_PER_MIN` if you run more.
