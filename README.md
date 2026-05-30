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

> **Scope:** designed for LAN / trusted networks. Not audited or hardened for direct internet exposure. For internet access, place the broker behind a VPN or tunnel.

---

## Table of Contents

- [What is it](#what-is-it)
- [Why](#why)
- [Architecture](#architecture)
- [Roles](#roles)
- [Security model](#security-model)
- [Requirements](#requirements)
- [Quick start](#quick-start)
- [Configuration reference](#configuration-reference)
- [Generating .env (bootstrap)](#generating-env-bootstrap)
- [TLS certificates](#tls-certificates)
- [Adding more senders](#adding-more-senders)
- [Project structure](#project-structure)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [Responsible use](#responsible-use)
- [License](#license)

---

## What is it

SelfDesk connects a **receiver** machine (where you sit) to one or more **sender** machines (the ones you control), through a lightweight **broker** you host yourself. The receiver watches the remote screens and sends mouse and keyboard input; the senders capture the screen and inject the received input.

None of the Windows machines open inbound ports. Native RDP (3389) stays closed. Everything travels over TLS with HMAC-SHA256 challenge-response authentication.

---

## Why

- **Zero inbound ports** on controlled machines — only outbound connections to the broker.
- **Native RDP (3389) closed** — minimal attack surface.
- **Fully self-hosted** — your data never passes through a third-party server.
- **Configuration as code** — everything in `.env`, generated locally, never committed.
- **Scale from 1 to N senders by config alone** — no code changes needed.
- **Pluggable codec** — JPEG to get started, hardware H.264 (Quick Sync / NVENC) later.
- **Any machine, any role** — the `ROLE` in `.env` defines broker, sender, or receiver.

---

## Architecture

```
  SENDER (C#/.NET Agent)               BROKER (Node.js)          RECEIVER (C#/WPF Viewer)
 ┌──────────────────────┐         ┌──────────────────────┐     ┌──────────────────────────┐
 │ Screen capture (DXGI)│         │ Authenticates conns  │     │ Renders stream(s)         │
 │ Encode (JPEG/H264)   │──TLS──▶ │ Registers by peer_id │◀TLS─│ Selects sender (list)     │
 │ Injects input        │(outbound│ Routes bytes         │outbnd│ Captures mouse + keyboard │
 └──────────▲───────────┘         │ (never decodes)      │     └─────────────┬────────────┘
            │                      └──────────────────────┘                   │
            └──────────────── INPUT_EVENT (peer_id = target sender) ──────────┘
```

Every message carries a `peer_id` (big-endian, 16 bytes in the header). The broker is a **dumb authenticated pipe**: it never decodes video, only routes bytes. Adding a second sender is pure configuration — no code changes.

### Wire protocol (summary)

Fixed 22-byte envelope on every message:

| Offset | Bytes | Field |
|--------|-------|-------|
| 0 | 1 | VERSION = `0x01` |
| 1 | 1 | TYPE (see table) |
| 2 | 16 | PEER\_ID (UTF-8, padded with `\0`) |
| 18 | 4 | LENGTH (uint32 big-endian) |
| 22 | N | PAYLOAD |

Handshake: `HELLO → CHALLENGE (32B nonce) → AUTH (HMAC-SHA256) → AUTH_OK`. The secret never travels over the wire. PING/PONG heartbeat every 5s; no PONG within 15s → connection closed.

---

## Roles

| Role | `ROLE` in `.env` | Platform | Connection direction |
|------|-----------------|----------|---------------------|
| Broker | `broker` | Linux (Node.js LTS) | listens on `LISTEN_PORT` |
| Sender | `sender` | Windows 10/11 | outbound only to broker |
| Receiver | `receiver` | Windows 10/11 | outbound only to broker |

The same repository clone can play any role — just run the corresponding bootstrap.

---

## Security model

- **TLS on every connection** with a LAN-local CA (no dependency on a public CA). The broker presents `server-cert.pem`; clients pin the CA via `TLS_CA_PATH`.
- **Challenge-response authentication (HMAC-SHA256)** — `SHARED_SECRET` never travels over the wire.
- **Zero inbound ports on Windows machines** — outbound connections to the broker only.
- **Native RDP (3389) disabled** as part of setup.
- **No secrets committed** — `.env`, keys, and certificates are in `.gitignore`.

This project assumes a trusted local network. It has not been audited for public internet exposure; in that scenario, use a VPN or tunnel in front of the broker.

Found a vulnerability? Please report it responsibly via a [private issue](https://github.com/Viniciusap/selfdesk/issues) before public disclosure.

---

## Requirements

| Component | Requirement |
|-----------|------------|
| Broker | Node.js LTS — installed automatically by `install.sh` if missing |
| Sender / Receiver | Windows 10/11, .NET 10 SDK — installed automatically by `install.ps1` if missing |
| Cert bootstrap | `openssl` in PATH (broker machine) — installed automatically by `install.sh` |
| Network | All machines on the same LAN |

> **Prerequisites are handled automatically** by the install scripts via `winget` (Windows) and `apt` / NodeSource (Linux). No manual pre-installation needed on a fresh machine.

---

## Quick start

### Option A — Pre-built binaries (recommended for most users)

Download the latest release from [GitHub Releases](https://github.com/Viniciusap/selfdesk/releases/latest):

| File | Machine |
|------|---------|
| `selfdesk-broker-linux-x64.tar.gz` | Broker (Linux) |
| `selfdesk-agent-win-x64.zip` | Sender (Windows) — includes FFmpeg DLLs for H264 |
| `selfdesk-viewer-win-x64.zip` | Receiver (Windows) — includes FFmpeg DLLs for H264 |

Then run `scripts/bootstrap.sh broker` (Linux) or `scripts\bootstrap.ps1 -Role <sender|receiver>` (Windows) from the extracted folder to generate your `.env`.

### Option B — Build from source

```bash
git clone https://github.com/Viniciusap/selfdesk.git
cd selfdesk
```

The setup is two steps per machine: **install** (deps + build) then **bootstrap** (config + secrets).

### 1. Broker (Linux)

```bash
# Step 1 — install deps and compile (auto-installs Node.js LTS if missing)
./scripts/install.sh broker

# Step 2 — generate .env, SHARED_SECRET, and TLS certificates
./scripts/bootstrap.sh broker
# Note the printed SHARED_SECRET and copy certs/ca-cert.pem to your Windows machines.

sudo ufw allow from <YOUR_SUBNET>/24 to any port <LISTEN_PORT> proto tcp
cd broker && npm start
```

### 2. Sender — machine to be controlled (Windows)

```powershell
# Step 1 — install deps, compile, and download FFmpeg DLLs for H264 (Phase 4)
.\scripts\install.ps1 -Role sender
# Add -SkipFFmpeg to skip FFmpeg download if using ENCODER=jpeg only.

# Step 2 — generate agent/.env
.\scripts\bootstrap.ps1 -Role sender
# Prompts for: broker host, SHARED_SECRET, AGENT_ID, encode parameters.

# Disable native RDP (PowerShell as administrator):
Set-ItemProperty 'HKLM:\System\CurrentControlSet\Control\Terminal Server' fDenyTSConnections 1
Disable-NetFirewallRule -DisplayGroup "Remote Desktop"

cd agent && dotnet run
```

### 3. Receiver — control machine (Windows)

```powershell
# Step 1 — install deps, compile, and download FFmpeg DLLs for H264 (Phase 4)
.\scripts\install.ps1 -Role receiver
# Add -SkipFFmpeg to skip FFmpeg download if using ENCODER=jpeg only.

# Step 2 — generate viewer/.env
.\scripts\bootstrap.ps1 -Role receiver
# Prompts for: broker host, SHARED_SECRET.

cd viewer && dotnet run
```

---

## Configuration reference

Each component reads a `.env` in its own directory (`broker/`, `agent/`, `viewer/`). These files are **generated by bootstrap** and **never committed** — the repository only versions the `*.env.example` files.

| Variable | broker | sender | receiver | Description |
|----------|:------:|:------:|:--------:|-------------|
| `ROLE` | ✓ | ✓ | ✓ | `broker` \| `sender` \| `receiver` |
| `SHARED_SECRET` | ✓ | ✓ | ✓ | Identical on all three machines; generated at the broker |
| `LISTEN_PORT` | ✓ | | | TLS listen port for the broker |
| `ALLOWED_SENDERS` | ✓ | | | CSV of permitted `AGENT_ID`s (e.g. `laptop-01,laptop-02`) |
| `TLS_CERT_PATH` | ✓ | | | Path to `server-cert.pem` (broker) |
| `TLS_KEY_PATH` | ✓ | | | Path to `server-key.pem` (broker) |
| `LOG_LEVEL` | ✓ | | | `debug` \| `info` \| `warn` \| `error` |
| `AGENT_ID` | | ✓ | | Unique sender identifier (must be in `ALLOWED_SENDERS`) |
| `BROKER_HOST` | | ✓ | ✓ | Broker IP or hostname |
| `BROKER_PORT` | | ✓ | ✓ | Broker port |
| `TLS_CA_PATH` | | ✓ | ✓ | Path to `ca-cert.pem` (pinning) |
| `TARGET_FPS` | | ✓ | | Target capture FPS (default: `30`) |
| `ENCODER` | | ✓ | | `jpeg` \| `qsv` \| `nvenc` |
| `JPEG_QUALITY` | | ✓ | | JPEG quality 1–100 (default: `75`) |

---

## Generating .env (bootstrap)

Instead of editing `.env` by hand, use the scripts in `scripts/`. They prompt for each value with sensible defaults and **never overwrite** an existing `.env` without confirmation.

**Linux / macOS:**
```bash
./scripts/bootstrap.sh broker     # generates broker/.env + SHARED_SECRET + certs/
./scripts/bootstrap.sh sender     # generates agent/.env
./scripts/bootstrap.sh receiver   # generates viewer/.env
```

**Windows:**
```powershell
.\scripts\bootstrap.ps1 -Role broker     # generates broker\.env (requires openssl in PATH)
.\scripts\bootstrap.ps1 -Role sender     # generates agent\.env
.\scripts\bootstrap.ps1 -Role receiver   # generates viewer\.env
```

The `SHARED_SECRET` is generated once at the broker and must be the **same** across all three `.env` files. The broker bootstrap prints it at the end for you to paste into the other machines.

---

## TLS certificates

Communication uses TLS with a **LAN-local CA** (no public CA). The broker bootstrap generates:

- `certs/ca-cert.pem` — LAN certificate authority. **Copy to every sender/receiver machine** (used for pinning via `TLS_CA_PATH`).
- `certs/server-cert.pem` / `certs/server-key.pem` — server keypair. The private key **never leaves the broker**.

The entire `certs/` folder is in `.gitignore`. Keys never go to the repository.

---

## Adding more senders

1. On the new machine, run bootstrap with role `sender` and choose a unique `AGENT_ID` (e.g. `laptop-02`).
2. Copy `certs/ca-cert.pem` from the broker to the new machine.
3. On the broker, add the new ID to `ALLOWED_SENDERS` (e.g. `laptop-01,laptop-02`) and restart.

No code changes required. The receiver automatically shows the new sender in the list.

---

## Project structure

```
selfdesk/
├── .github/workflows/ci.yml   # CI: broker build+tests (Ubuntu) + .NET (Windows)
├── scripts/
│   ├── bootstrap.sh           # Generates .env + certs (Linux/macOS)
│   └── bootstrap.ps1          # Generates .env + certs (Windows)
├── broker/                    # Node.js + TypeScript — authenticated relay
│   ├── src/
│   └── .env.example
├── agent/                     # C# / .NET 10 — sender (capture + encode + inject)
│   ├── src/
│   └── .env.example
├── viewer/                    # C# / WPF / .NET 10 — receiver (render + input)
│   ├── src/
│   └── .env.example
├── shared/protocol/           # Protocol constants mirrored in TS and C#
├── selfdesk.slnx              # .NET solution (agent + viewer + tests)
├── LICENSE
└── README.md
```

---

## Roadmap

| Phase | Deliverable | Status |
|-------|-------------|--------|
| 0 | Skeleton, TLS, HMAC auth, heartbeat, open-source packaging | ✅ Complete |
| 1 | MVP: one-way JPEG video + mouse input | ✅ Code complete (hardware gate pending) |
| 2 | Keyboard + latency tuning + RTT measurement | ✅ Code complete (hardware gate pending) |
| 3 | Multiple senders + sender selector in viewer | ✅ Code complete (hardware gate pending) |
| 4 | Hardware codec (Quick Sync / NVENC via FFmpeg) | ✅ Code complete (requires FFmpeg DLLs + hardware to activate) |
| 5 | Agent as Windows service (lock screen / UAC) | ✅ Code complete (hardware gate pending) |

---

## Contributing

Contributions are welcome. Workflow:

1. Open an issue to discuss large changes before implementing.
2. Fork + descriptive branch (`feat/name`, `fix/name`).
3. Follow project conventions:
   - **Big-endian protocol** on every multi-byte field; `shared/protocol/` must stay in sync between TS and C#.
   - **No hardcoded** secrets, IPs, or ports — everything via `.env`.
   - **DI + IOptions** in .NET apps; no manual global singletons.
   - **Structured logging** (`pino` in broker, `ILogger<T>` in .NET).
   - **TLS mandatory** on every connection; no plaintext fallback.
   - **Interfaces for Windows-only APIs** (`IScreenCapturer`, `IInputInjector`) — unit tests use fakes.
   - **Video conflation:** Channel capacity 1; old frame is dropped, never queued.
4. `npm test` in broker must be green. `dotnet test` on the solution must be green.
5. `git status` after build must not show `.env`, `.pem`, `.key`, `bin/`, `obj/`, or `node_modules/`.
6. Commit messages in Portuguese, conventional style (e.g. `feat: peer_id routing in broker`).
7. Open the PR against `main`.

---

## Responsible use

SelfDesk is a remote access tool intended exclusively for machines that **you own or have explicit authorization to access**.

Using this tool for **unauthorized access** to third-party systems, **non-consensual surveillance**, or any form of privacy invasion is expressly prohibited and is the sole responsibility of the user, potentially constituting a criminal offense under applicable law.

The maintainers of this project **accept no liability** for any misuse of the tool. Use it ethically and respect others' privacy.

---

## License

Distributed under the MIT License. See [LICENSE](LICENSE).
