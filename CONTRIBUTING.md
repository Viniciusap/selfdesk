# Contributing to SelfDesk

Contributions are welcome — bug fixes, new features, tests, and docs alike.

## Workflow

1. **Open an issue** before starting large changes so we can align on the approach.
2. Fork the repository and create a descriptive branch (`feat/name`, `fix/name`).
3. Follow the conventions below.
4. Open the PR against `master`.

## Running tests

```bash
# Broker
cd broker && npm ci && npm test

# .NET (sender + viewer + shared)
dotnet test selfdesk.slnx
```

Both must be green before opening a PR.

## Code conventions

### Protocol

- **All multi-byte fields are big-endian.** No exceptions.
- `shared/protocol/protocol.ts` and `shared/dotnet/MessageType.cs` must stay in sync. The CI job `Verify TS ↔ C# protocol sync` checks this on every push.
- Any new message type needs an entry in both files and a test.

### .NET

- **DI + IOptions** throughout — no manual global singletons.
- **Interfaces for Windows-only APIs** (`IScreenCapturer`, `IFrameEncoder`, `IInputInjector`) so unit tests can use fakes without hitting DXGI or `SendInput`.
- **Structured logging** via `ILogger<T>` — no `Console.WriteLine`.
- **Video conflation:** the sender's `Channel` has capacity 1 (`BoundedChannelFullMode.DropOldest`). Never queue video frames.
- Mouse coordinates are always **normalized 0–65535** (`MOUSEEVENTF_ABSOLUTE`). Never use logical pixels.

### Broker (TypeScript)

- **Structured logging** via `pino`.
- No plaintext fallback — TLS is mandatory even in development.
- The broker never decodes video. It routes bytes by `peer_id` only.

### General

- No hardcoded secrets, IPs, or ports — everything via `.env`.
- **Never commit** `.env`, `*.pem`, `*.key`, `bin/`, `obj/`, or `node_modules/`.
- Commit messages in English, conventional style: `feat: ...`, `fix: ...`, `docs: ...`.

## Project structure

```
selfdesk/
├── broker/          # Node.js + TypeScript — authenticated relay
├── sender/          # C# / .NET 10 — screen capture + input injection
├── viewer/          # C# / WPF / .NET 10 — remote control UI
├── shared/
│   ├── protocol/    # protocol.ts (TS) — authoritative message type constants
│   └── dotnet/      # MessageType.cs + WireProtocol.cs — C# mirror
├── scripts/         # install-*.sh/ps1, bootstrap.sh/ps1, deploy helpers
└── selfdesk.slnx    # .NET solution
```

## Good first issues

See issues labelled [`good first issue`](https://github.com/Viniciusap/selfdesk/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22).
