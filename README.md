<div align="center">

# SelfDesk

**Dev-first, self-hosted remote access for your LAN.**  
An open-source alternative to AnyDesk and TeamViewer — no third-party cloud, no exposed RDP, no inbound ports on controlled machines.

[![CI](https://github.com/Viniciusap/selfdesk/actions/workflows/ci.yml/badge.svg)](https://github.com/Viniciusap/selfdesk/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Node.js LTS](https://img.shields.io/badge/Node.js-LTS-339933)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-lightgrey)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](https://github.com/Viniciusap/selfdesk/pulls)

</div>

<div align="center">

![SelfDesk demo](demo.gif)

*Viewer (left) controlling a remote Windows machine via a self-hosted broker*

</div>

> **Scope:** designed for LAN / trusted networks. Not audited for direct internet exposure. For remote access over the internet, place the broker behind a VPN or tunnel.

---

## Install / Update

One command takes a fresh machine from zero to a configured, running role — it asks which role this machine is (broker / sender / viewer), downloads the matching release build, and walks through bootstrap (host/port/secret/encoder). Existing `.env` and certificates are never overwritten without confirmation.

```bash
# Linux, WSL, Git Bash (auto-detects platform, delegates to PowerShell on Windows)
curl -fsSL https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/setup.sh | bash
```

```powershell
# Windows PowerShell
irm https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/setup.ps1 | iex
```

Non-interactive (scripted installs): set `SELFDESK_ROLE=broker|sender|viewer` (bash) or `$env:SELFDESK_ROLE` (PowerShell) before running.

<details>
<summary>Advanced — per-role scripts (custom install dir, systemd/autostart flags, etc.)</summary>

```bash
# Broker — Linux, WSL, Git Bash
curl -fsSL https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/install-broker.sh | bash
```

```powershell
# Broker — Windows PowerShell (no bash)
irm https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/install-broker.ps1 | iex

# Sender — Windows (run as Administrator)
irm https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/install-sender.ps1 | iex

# Viewer — Windows
irm https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/install-viewer.ps1 | iex
```

Each of these still needs a separate `bootstrap.sh`/`bootstrap.ps1 -Role <role>` run afterward — see [Quick start](#quick-start) below. `setup.sh`/`setup.ps1` chain both steps automatically; the per-role scripts are for when you want just one of them (e.g. re-running bootstrap only, or a non-default install directory).

</details>

---

## What it does

You run a small **broker** on any Linux box (a Raspberry Pi handles it fine — ARM64 and x64 tarballs are both published on every release). Your Windows machines connect outbound to it — nothing listens for inbound connections, no third-party cloud, nothing phones home.

- **Screen capture and remote control** — mouse, keyboard, scroll wheel
- **Clipboard sync** — copy on one machine, paste on the other, bidirectional
- **File transfer** — drag files onto the video surface, progress bar included
- **Wake-on-LAN** — wake offline machines from the viewer sidebar
- **Hardware H.264** — Quick Sync (Intel) or NVENC (NVIDIA) via FFmpeg, selectable by `.env`
- **Multiple senders** — control several machines from one viewer, switch with a click
- **Automatic reconnect** — exponential backoff on broker restart or network hiccup
- **Web viewer** *(read-only milestone)* — watch a sender's screen from any browser over WSS, no install required

---

## Quick start

Setup order: **broker first**, then sender, then viewer. The broker generates the `SHARED_SECRET` and `ca-cert.pem` that the other machines need.

### Step 1 — Broker

<details open>
<summary><b>Linux (recommended)</b></summary>

```bash
curl -fsSL https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/install-broker.sh | bash
cd ~/selfdesk
./scripts/bootstrap.sh broker
```

Note the printed `SHARED_SECRET`. Then open the firewall and start:

```bash
sudo ufw allow from <YOUR_SUBNET>/24 to any port <LISTEN_PORT> proto tcp
node dist/index.js
```

</details>

<details>
<summary><b>Windows</b></summary>

```powershell
irm https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/install-broker.ps1 | iex
cd $env:USERPROFILE\selfdesk-broker
powershell -File scripts\bootstrap.ps1 -Role broker
node dist\index.js
```

</details>

<details>
<summary><b>Docker</b></summary>

The bootstrap step still has to run once on the host to generate `certs/` and
`broker/.env` — the image itself carries no secrets.

```bash
git clone https://github.com/Viniciusap/selfdesk.git
cd selfdesk
./scripts/bootstrap.sh broker
docker compose up -d
```

`docker-compose.yml` builds `broker/Dockerfile` (multi-stage, non-root user) and
mounts `certs/` and `broker/.env` read-only. Note that `broker/.env`'s
`TLS_CERT_PATH`/`TLS_KEY_PATH` default to `../certs/...` (one level up from the
`.env` file), which inside the container is `/certs/...` — hence the compose
file mounts `./certs` to `/certs`, not `/app/certs`.

A pre-built image is published to `ghcr.io/viniciusap/selfdesk-broker` on every
tagged release. To use it directly instead of building locally, edit the
`build:` block out of `docker-compose.yml` and set `image:` to a specific tag.

If `HEALTH_PORT` is set in `broker/.env` (see [Configuration reference](#configuration-reference)),
the container's `HEALTHCHECK` polls `GET /health` automatically.

</details>

After the broker starts, **copy `certs/ca-cert.pem`** to each Windows machine — senders and viewers need it to verify the broker's TLS certificate.

---

### Step 2 — Sender *(machine to be controlled, Windows)*

```powershell
irm https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/install-sender.ps1 | iex

# Copy ca-cert.pem from the broker into C:\tools\selfdesk-sender\, then:
cd C:\tools\selfdesk-sender
powershell -File scripts\bootstrap.ps1 -Role sender
# → prompts for broker host, SHARED_SECRET, SENDER_ID, encoder (jpeg / qsv / nvenc)

.\SelfDesk.Sender.exe
```

---

### Step 3 — Viewer *(control machine, Windows)*

```powershell
irm https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/install-viewer.ps1 | iex

# Copy ca-cert.pem from the broker into C:\tools\selfdesk-viewer\, then:
cd C:\tools\selfdesk-viewer
powershell -File scripts\bootstrap.ps1 -Role receiver
# → prompts for broker host and SHARED_SECRET

.\SelfDesk.Viewer.exe
```

---

### Build from source

<details>
<summary>Expand</summary>

```bash
git clone https://github.com/Viniciusap/selfdesk.git
cd selfdesk
```

**Broker (Linux)**
```bash
./scripts/install.sh broker      # installs Node.js LTS if missing, compiles
./scripts/bootstrap.sh broker    # generates .env, SHARED_SECRET, certs/
sudo ufw allow from <YOUR_SUBNET>/24 to any port <LISTEN_PORT> proto tcp
cd broker && npm start
```

**Sender (Windows)**
```powershell
.\scripts\install.ps1 -Role sender     # installs .NET 10 SDK if missing, compiles
.\scripts\bootstrap.ps1 -Role sender
cd sender && dotnet run
```

**Viewer (Windows)**
```powershell
.\scripts\install.ps1 -Role receiver
.\scripts\bootstrap.ps1 -Role receiver
cd viewer && dotnet run
```

</details>

---

## How it works

```
  SENDER (C#/.NET)                BROKER (Node.js)          VIEWER (C#/WPF)
 ┌─────────────────┐         ┌──────────────────────┐     ┌─────────────────┐
 │ Screen capture  │         │ Authenticates conns  │     │ Renders stream  │
 │ Encode (JPEG /  │──TLS──▶ │ Routes by peer_id    │◀TLS─│ Captures input  │
 │ H264)           │outbound │ Never decodes video  │outbd│ Picks sender    │
 │ Inject input    │         └──────────────────────┘     └────────┬────────┘
 └────────▲────────┘                                               │
          └──────────────── INPUT_EVENT ────────────────────────────┘
```

The broker is a **dumb authenticated pipe**: it routes bytes by `peer_id` and never decodes video. Adding a second sender is pure config — no code changes.

### Security

- **TLS 1.3 on every connection** with a LAN-local CA (no public CA dependency). Clients pin the CA via `TLS_CA_PATH`.
- **HMAC-SHA256 challenge-response** — `SHARED_SECRET` never travels over the wire.
- **Zero inbound ports on Windows machines** — outbound connections to the broker only.
- **No secrets committed** — `.env`, keys, and certificates are in `.gitignore`.

Found a vulnerability? Open an issue or email vinicius.ap48@gmail.com.

---

## Configuration reference

Bootstrap generates the `.env` files for you — you rarely need to edit them by hand. The broker bootstrap prints the `SHARED_SECRET` for you to paste into the sender and viewer `.env` files.

| Variable | broker | sender | viewer | Description |
|----------|:------:|:------:|:------:|-------------|
| `ROLE` | ✓ | ✓ | ✓ | `broker` \| `sender` \| `receiver` |
| `SHARED_SECRET` | ✓ | ✓ | ✓ | Identical on all three; generated at the broker |
| `LISTEN_PORT` | ✓ | | | TLS listen port |
| `ALLOWED_SENDERS` | ✓ | | | Comma-separated list of permitted `SENDER_ID`s |
| `TLS_CERT_PATH` | ✓ | | | Path to `server-cert.pem` |
| `TLS_KEY_PATH` | ✓ | | | Path to `server-key.pem` |
| `LOG_LEVEL` | ✓ | | | `debug` \| `info` \| `warn` \| `error` |
| `SENDER_ID` | | ✓ | | Unique identifier (must be in `ALLOWED_SENDERS`) |
| `BROKER_HOST` | | ✓ | ✓ | Broker IP or hostname |
| `BROKER_PORT` | | ✓ | ✓ | Broker port |
| `TLS_CA_PATH` | | ✓ | ✓ | Path to `ca-cert.pem` (CA pinning) |
| `ENCODER` | | ✓ | | `jpeg` \| `qsv` \| `nvenc` |
| `TARGET_FPS` | | ✓ | | Capture FPS (default: `30`) |
| `JPEG_QUALITY` | | ✓ | | JPEG quality 1–100 (default: `75`) |
| `BENCHMARK` | | ✓ | ✓ | Opt-in per-frame latency CSV (default: `false`) — see [Benchmarking](#benchmarking) |
| `BENCHMARK_LOG_PATH` | | ✓ | ✓ | CSV output path (default: `benchmark-sender.csv` / `benchmark-viewer.csv`) |
| `WS_PORT` | ✓ | | | Opt-in WSS + web viewer (unset = disabled) — see [Web viewer](#web-viewer) |
| `HEALTH_PORT` | ✓ | | | Opt-in plain-HTTP health check (unset = disabled) |
| `METRICS_PORT` | ✓ | | | Opt-in plain-HTTP Prometheus `/metrics` (unset = disabled) |

---

## Benchmarking

Set `BENCHMARK=true` in both `sender/.env` and `viewer/.env` to write a per-frame
latency CSV on each side (capture/encode on the sender, wire/decode/total on the
viewer), correlated by the `VIDEO_FRAME` `TIMESTAMP` field. After a run, join and
summarize with:

```bash
node scripts/benchmark.js sender/benchmark-sender.csv viewer/benchmark-viewer.csv
```

Prints p50/p95/p99 per stage (capture, encode, wire, decode, total). Off by
default — writing a CSV row per frame is unnecessary overhead outside of
tuning/regression work.

---

## Web viewer

A browser-based receiver — watch a sender's screen from any device with no
install, over the same TLS-authenticated protocol as the native viewer. Set
`WS_PORT` in `broker/.env` (see [Configuration reference](#configuration-reference)),
restart the broker, then open `https://<broker-host>:<WS_PORT>/` in a browser.
Enter the broker host, the WSS port, and the `SHARED_SECRET` to connect.

**Milestone 1 (current): read-only.** Video display only, JPEG codec only —
no mouse/keyboard input yet, and H264 senders show a "use the native viewer"
message instead of a picture. The web viewer authenticates with the exact
same HELLO/CHALLENGE/AUTH handshake as the WPF viewer and is routed through
the same broker code path (`broker/src/connection.ts`'s `FramedSocket`
interface is transport-agnostic between the TCP and WebSocket listeners), so
a sender doesn't know or care which kind of viewer is watching it.

Source: `broker/web/` (vanilla JS, no build step, no dependencies).

---

## Adding more senders

1. On the new machine, run `install-sender.ps1`, copy `ca-cert.pem`, run bootstrap with a unique `SENDER_ID` (e.g. `laptop-02`).
2. On the broker, add the new ID to `ALLOWED_SENDERS` (e.g. `laptop-01,laptop-02`) and restart.

The viewer automatically shows the new sender in the list. No code changes.

---

## Project structure

```
selfdesk/
├── .github/workflows/
│   ├── ci.yml          # broker tests (Ubuntu) + .NET build+tests (Windows)
│   └── release.yml     # builds release zips/tarballs on version tags
├── scripts/
│   ├── install-broker.sh   # broker install/update — Linux/WSL/Git Bash
│   ├── install-broker.ps1  # broker install/update — Windows PowerShell
│   ├── install-sender.ps1  # sender install/update — Windows
│   ├── install-viewer.ps1  # viewer install/update — Windows
│   ├── bootstrap.sh        # generates .env + TLS certs (Linux)
│   └── bootstrap.ps1       # generates .env + TLS certs (Windows)
├── broker/             # Node.js + TypeScript — authenticated relay
├── sender/             # C# / .NET 10 — capture + encode + inject input
├── viewer/             # C# / WPF / .NET 10 — render stream + capture input
├── shared/
│   ├── protocol/       # protocol.ts — authoritative message type constants
│   └── dotnet/         # MessageType.cs + WireProtocol.cs — C# mirror
└── selfdesk.slnx       # .NET solution (sender + viewer + tests)
```

---

## Roadmap

All core features are implemented. What's open:

| Area | Issues |
|---|---|
| Platform | [macOS support](https://github.com/Viniciusap/selfdesk/issues/1), [Linux sender](https://github.com/Viniciusap/selfdesk/issues/11), [ARM64 broker build](https://github.com/Viniciusap/selfdesk/issues/7) |
| Distribution | [Docker image](https://github.com/Viniciusap/selfdesk/issues/6), [Web viewer](https://github.com/Viniciusap/selfdesk/issues/12) |
| UX | [Multi-monitor switching](https://github.com/Viniciusap/selfdesk/issues/20), [Remote cursor shape](https://github.com/Viniciusap/selfdesk/issues/22), [Ctrl+Alt+Del](https://github.com/Viniciusap/selfdesk/issues/13) |
| Protocol | [Clipboard images](https://github.com/Viniciusap/selfdesk/issues/8), [Mic passthrough](https://github.com/Viniciusap/selfdesk/issues/21) |
| Ops | [Prometheus metrics](https://github.com/Viniciusap/selfdesk/issues/19), [Log rotation](https://github.com/Viniciusap/selfdesk/issues/18) |

See all open issues [here](https://github.com/Viniciusap/selfdesk/issues).

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for conventions, test commands, and project structure details.

Good entry points: [`good first issue`](https://github.com/Viniciusap/selfdesk/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22) · [`help wanted`](https://github.com/Viniciusap/selfdesk/issues?q=is%3Aissue+is%3Aopen+label%3A%22help+wanted%22)

---

## License

Distributed under the MIT License. See [LICENSE](LICENSE).

SelfDesk is intended for machines you own or have explicit authorization to access.
