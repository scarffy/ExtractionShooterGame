# Dedicated servers and the server browser

A dedicated server is a headless build of the game that runs the FishNet server with no local player. Players find it
in the main menu's **Browse servers** list, which is read from the master server (`MasterServer/`, see its
`README.md` and `MASTER_API.md`). The master only keeps the list; game traffic goes straight from the player to the
game server over **UDP** (FishNet Tugboat, default port 7770).

```
 game server ──heartbeat every 10 s (HTTPS)──▶ master server ◀──GET /api/servers── game client
      ▲                                                                                │
      └──────────────────────── UDP game traffic (Tugboat, port 7770) ◀────────────────┘
```

How it works in the game:

- `ServerBootstrap` (on the NetworkManager object) starts the server automatically when the game runs headless (a
  Dedicated Server build, or any player started with `-batchmode`). It reads `server.cfg` and the command line, caps
  the frame rate at 60 (an idle server would otherwise use a whole CPU core) and logs the settings it uses.
- `MasterServerAnnouncer` (same object) sends a heartbeat to the master every `heartbeatSeconds`: name, port, region,
  map, the map's difficulties, signed-in players, raid size, the running raid's difficulty and time left, and the game
  version. On shutdown it removes the server from the list; if the process is killed, the master drops it after 30 s.
  A **host** (Host game in the menu) is never listed.
- Clients only see and join servers with the **same game version** (Player Settings → Version, `Application.version`).
  Bump it whenever a new build cannot play with the previous one, and deploy client and server builds together.

## 1. Build the Linux server (Unity 6)

1. Add the build module once: **Unity Hub → Installs → (your 6000.3 install) ⚙ → Add modules → Linux Dedicated
   Server Build Support** (it is under *Platforms*; tick it and install). If you build with IL2CPP you also need
   **Linux Build Support (IL2CPP)**; the Mono scripting backend is simplest for a server and needs nothing else.
2. In the editor: **File → Build Profiles → Add Build Profile → Linux Server → Add Build Profile**, select it and
   **Switch Profile**. (If Linux Server says the module is missing, use **Install with Unity Hub** from there.)
3. Make sure the game scene (`Sandbox`) is in the profile's scene list, and check **Player Settings → Version**: it must
   match the client build players use.
4. **Build** into an empty folder, e.g. `Builds/LinuxServer/`. You get `Server.x86_64` (named after your build),
   `Server_Data/` and `UnityPlayer.so`. Copy the whole folder to the VPS.

Notes:

- The *Dedicated Server* subtarget defines `UNITY_SERVER` automatically for scripts: no manual scripting define.
  The game already uses it (no menus, no rendering-only work, `ServerBootstrap` starts the server).
- The server build talks to the master with `UnityWebRequest`. Use an **https://** master address in production.
  Plain `http://` is refused unless **Player Settings → Other Settings → Allow downloads over HTTP** is
  *Always allowed* (or *Allowed in development builds* for development builds); the log then says
  "Insecure connection not allowed". The same setting applies to clients reading the list.

## 2. Run it

```bash
cd /opt/extraction/server1
chmod +x Server.x86_64
./Server.x86_64 -batchmode -nographics -logFile - -config server.cfg
```

- `-batchmode -nographics`: no window, no GPU. (`UNITY_SERVER` builds are headless anyway; keep the flags, they are
  harmless and also make a normal player build run as a server.)
- `-logFile -`: log to stdout (systemd's journal picks it up).
- `-config <path>`: the config file. Without it the server looks for `server.cfg` in the **working folder**, then in
  the **executable's folder**.
- Any config key can also be given on the command line and wins over the file: `-name "Kim's server" -port 7771
  -region sg -master https://master.example.com -token <secret> -publicHost 203.0.113.10 -maxPlayers 12
  -heartbeatSeconds 10`. `--key value` and `-key=value` work too (use `-token=<secret>` if the secret starts with `-`).
- The old `-port 7770` argument still works.

On startup the log shows the effective settings (the token only as `(set)`):

```
[ServerBootstrap] Headless mode, starting server. name="Kim's server #1" port=7770 region=sg master=https://master.example.com token=(set) publicHost=(auto) maxPlayers=(lobby) heartbeatSeconds=10 config=/opt/extraction/server1/server.cfg port in use=7770 frameRate=60
[MasterServerAnnouncer] Listing this server on https://master.example.com every 10 s (id 6f1c2a9e…).
```

Bad values are logged as `[ServerBootstrap] Server config: server.cfg line 3: port: a number from 1 to 65535.` and the
default is kept. Heartbeat problems are logged once per failure streak (e.g. `Heartbeat … failed: HTTP 400 (port: …)`);
a wrong token logs `The master server rejected the token (401)`. While the master is unreachable the server retries
after 10, 20, 40, then every 60 s, and logs again when it gets through.

### Sample `server.cfg`

```ini
# Extraction shooter dedicated server. key=value; '#', ';' or '//' start a comment.
name=Kim's server #1
port=7770
region=sg
# Master server base URL. Leave empty (or remove) to run unlisted: players then join by address and port.
master=https://master.example.com
# Must equal SERVER_TOKEN on the master. Keep this file readable only by the server user (chmod 600).
token=change-me
# Only when the master would see another address than the one players should use
# (NAT, same VPS as the master, several IPs). Leave empty otherwise.
publicHost=
# Raid size override (1-128). Default: the lobby's (12). The map has 12 spawn points.
#maxPlayers=12
# Seconds between heartbeats (2-25; the master forgets a server after 30 s).
heartbeatSeconds=10
```

| Key | Default | Notes |
|---|---|---|
| `name` | `Extraction server` | 1–40 characters, shown in the server list |
| `port` | Tugboat's (7770) | UDP game port, 1–65535 |
| `region` | empty | `[a-z0-9-]`, up to 16 characters (`sg`, `my-kl`); players can filter by it |
| `master` | empty | `http(s)://host[:port]`; empty = not listed |
| `token` | empty | the master's `SERVER_TOKEN`; never logged |
| `publicHost` | empty | hostname or IP sent as `host`; empty = the master uses the heartbeat's source address |
| `maxPlayers` | lobby (12) | players per raid; the list shows `players/maxPlayers` |
| `heartbeatSeconds` | 10 | 2–25 |

## 3. Keep it running with systemd

Create a user without a login shell and put the build in `/opt/extraction/server1`:

```bash
sudo useradd --system --home /opt/extraction --shell /usr/sbin/nologin extraction
sudo mkdir -p /opt/extraction/server1 && sudo cp -r Builds/LinuxServer/* /opt/extraction/server1/
sudo cp server.cfg /opt/extraction/server1/ && sudo chmod 600 /opt/extraction/server1/server.cfg
sudo chown -R extraction:extraction /opt/extraction
```

`/etc/systemd/system/extraction-server@.service` (a template: `%i` is the instance name, so one file serves every
server on the machine):

```ini
[Unit]
Description=Extraction shooter dedicated server (%i)
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=extraction
Group=extraction
WorkingDirectory=/opt/extraction/%i
# Profiles (player stashes) go to $XDG_CONFIG_HOME/unity3d/<Company>/<Product>/Profiles: one folder per instance.
Environment=XDG_CONFIG_HOME=/opt/extraction/%i/data
ExecStart=/opt/extraction/%i/Server.x86_64 -batchmode -nographics -logFile - -config /opt/extraction/%i/server.cfg
Restart=on-failure
RestartSec=5
# SIGTERM lets Unity quit cleanly: profiles are saved on every change and the server removes itself from the list.
KillSignal=SIGTERM
TimeoutStopSec=20
NoNewPrivileges=true
LimitNOFILE=65536

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now extraction-server@server1
journalctl -u extraction-server@server1 -f        # live log
sudo systemctl restart extraction-server@server1  # after copying a new build
```

## 4. Firewall

Players connect to the game port over **UDP**. Open it on the VPS and in the provider's firewall / security group
(DigitalOcean, Vultr, AWS, etc. have their own, separate from `ufw`):

```bash
sudo ufw allow 7770/udp comment 'extraction server1'
sudo ufw allow 7771/udp comment 'extraction server2'   # one line per server
sudo ufw status
```

The game server needs no inbound TCP. It only makes outbound HTTPS calls to the master (allowed by default). The
master's own machine needs TCP 80/443 (see `MasterServer/README.md`).

## 5. Where player profiles are saved

Profiles live on the server in `Application.persistentDataPath/Profiles`. On a Linux player that is
`$XDG_CONFIG_HOME/unity3d/<Company Name>/<Product Name>/Profiles`, and `$XDG_CONFIG_HOME` defaults to `~/.config`:
`~/.config/unity3d/<Company Name>/<Product Name>/Profiles` of the user running the server (Company and Product Name
are in Player Settings). With the systemd unit above (which sets `XDG_CONFIG_HOME`) it is
`/opt/extraction/server1/data/unity3d/<Company Name>/<Product Name>/Profiles`.

Back this folder up (`tar czf profiles-$(date +%F).tgz -C /opt/extraction/server1/data .`). Profiles are per server:
a player's stash on server1 is separate from server2.

## 6. Several servers on one VPS

Each server is one process with its own **port**, **config** and **profile folder**:

```bash
sudo cp -r /opt/extraction/server1 /opt/extraction/server2   # same build, then edit server2/server.cfg:
#   name=Kim's server #2
#   port=7771
sudo rm -rf /opt/extraction/server2/data                       # start server2 with empty profiles
sudo chown -R extraction:extraction /opt/extraction/server2
sudo systemctl enable --now extraction-server@server2
sudo ufw allow 7771/udp
```

- Never let two servers share one profile folder (both would write the same files): the template unit gives each
  instance its own `XDG_CONFIG_HOME`. If you start servers by hand, set `XDG_CONFIG_HOME` per server or run them as
  different users.
- Budget roughly one CPU core and 1–1.5 GB RAM per busy server (60 frames per second with bots and physics).
- The master accepts 60 heartbeats per minute per IP by default, about 10 servers per machine at 10 s; see
  `RATE_HEARTBEAT_PER_MIN` in the master's README for more.
- If the game servers run on the **same VPS as the master**, the master sees their heartbeats come from `127.0.0.1` or
  a Docker address: set `publicHost` to the VPS's public IP or DNS name in every `server.cfg`.

## 7. How players find it

1. Main menu → **Browse servers**.
2. **Master server**: enter the master's address (e.g. `https://master.example.com`) once and press **Refresh**. It is
   saved for next time; a fresh install starts with the GameMenu's *Default Master Url* (`http://localhost:8080` in
   the generated scene; change it in the scene, or in `gen_assets.py`, before shipping a client build).
3. The list shows name, map, difficulties, players, raid status (time left), region and ping; it refreshes every 15 s.
   Search, region and sort (most players / lowest ping) filter it; **Show other versions** also lists servers that
   run another build (they cannot be joined).
4. **Join** connects to the server's address and port with the player name typed in the main menu.

Ping is an ICMP echo to the server's IPv4 address. Many VPS providers allow it; if ICMP is blocked the ping shows `—`
(joining still works). Players can still join any server by address with **Join game** in the main menu.

## 8. Test locally on a Mac

You need Node 20+ (`brew install node`).

**A. The browser against fake servers** (no game server):

```bash
cd MasterServer
npm install
npm start                                            # master on http://localhost:8080 (dev mode, no token)
# second terminal:
node scripts/fake-server.js --count 3 --raid --region sg --version <your Player Settings version>
```

In Unity: allow plain HTTP for local testing (**Player Settings → Other Settings → Allow downloads over HTTP →
Always allowed**), press Play, **Browse servers**, master `http://localhost:8080`, **Refresh**. Three fake servers
appear (joining one fails: they open no game port). Without `--version` the fakes report `0.5.0`: tick
**Show other versions** to see them.

**B. A real server in the editor**: with the master running, press Play, open **Browse servers** once so the master
address is saved, go **Back** and press **Dedicated server (dev)**. After a second the server lists itself (it uses
the browser's master address when there is no `server.cfg` in the project folder). From a second instance (a built
client, or a second editor via a project clone), Browse servers → Join.

**C. A headless Mac build**: build the normal macOS player (or the *macOS Server* profile, which needs the *Mac
Dedicated Server Build Support* module), then:

```bash
./Builds/Mac/Game.app/Contents/MacOS/Game -batchmode -nographics -logFile - \
  -name "Mac test" -port 7771 -master http://localhost:8080
```

Join it from the editor's server browser. Its profiles go to
`~/Library/Application Support/<Company Name>/<Product Name>/Profiles` (or `…/unity.<company>.<product>/Profiles` if
that folder does not exist yet) on the Mac.

## Troubleshooting

- **Server not in the list:** check the server log for `[MasterServerAnnouncer]` lines. `HTTP 401` = token mismatch,
  `HTTP 400 (<field>: …)` = a bad value, `Insecure connection not allowed` = http:// master with HTTP disallowed. No
  announcer line at all = `master` is not set. Then check the client's version matches (or tick Show other versions).
- **Listed but Join times out:** the UDP port is closed (ufw or the provider firewall), or the list shows a wrong
  host: set `publicHost`.
- **"Could not reach the server list"** in the browser: wrong master address, master down, or plain http:// refused.
- **High CPU on an idle server:** make sure it runs headless (the log says `Headless mode, starting server`); the frame
  rate is then capped at 60.
