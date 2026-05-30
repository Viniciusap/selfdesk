<div align="center">

# SelfDesk

**Acesso remoto dev-first e self-hosted para sua LAN.**  
Alternativa open source ao AnyDesk e TeamViewer — sem nuvem de terceiros, sem RDP exposto, sem abrir portas de entrada nas máquinas controladas.

[![CI](https://github.com/Viniciusap/selfdesk/actions/workflows/ci.yml/badge.svg)](https://github.com/Viniciusap/selfdesk/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Node.js LTS](https://img.shields.io/badge/Node.js-LTS-339933)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-lightgrey)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](https://github.com/Viniciusap/selfdesk/pulls)

</div>

> **Escopo:** projetado para LAN / redes confiáveis. Não auditado nem endurecido para exposição direta à internet. Para acesso pela internet, coloque o broker atrás de uma VPN ou túnel.

---

## Índice

- [O que é](#o-que-é)
- [Por quê](#por-quê)
- [Arquitetura](#arquitetura)
- [Papéis](#papéis)
- [Modelo de segurança](#modelo-de-segurança)
- [Requisitos](#requisitos)
- [Início rápido](#início-rápido)
- [Referência de configuração](#referência-de-configuração)
- [Geração do .env (bootstrap)](#geração-do-env-bootstrap)
- [Certificados TLS](#certificados-tls)
- [Adicionar mais emissores](#adicionar-mais-emissores)
- [Estrutura do projeto](#estrutura-do-projeto)
- [Roadmap](#roadmap)
- [Contribuindo](#contribuindo)
- [Uso responsável](#uso-responsável)
- [Licença](#licença)

---

## O que é

SelfDesk conecta uma máquina **receptora** (onde você senta) a uma ou mais máquinas **emissoras** (as que você controla), através de um **broker** leve que você mesmo hospeda. O receptor assiste às telas remotas e envia mouse e teclado; os emissores capturam a tela e injetam o input recebido.

Nenhuma das máquinas Windows abre porta de entrada. O RDP nativo (3389) permanece fechado. Tudo trafega em TLS com autenticação por desafio-resposta HMAC-SHA256.

---

## Por quê

- **Zero porta de entrada** nas máquinas controladas — só conexões de saída ao broker.
- **RDP nativo (3389) fechado** — superfície de ataque mínima.
- **Self-hosted total** — seus dados nunca passam por servidor de terceiros.
- **Configuração como código** — tudo no `.env`, gerado localmente, nunca commitado.
- **Escala de 1 para N emissores só por configuração** — sem mudar código.
- **Codec plugável** — JPEG para arrancar, H.264 por hardware (Quick Sync / NVENC) depois.
- **Qualquer máquina, qualquer papel** — o `ROLE` no `.env` define broker, emissor ou receptor.

---

## Arquitetura

```
  EMISSOR (Agente C#/.NET)            BROKER (Node.js)          RECEPTOR (Viewer C#/WPF)
 ┌──────────────────────┐         ┌──────────────────────┐     ┌──────────────────────────┐
 │ Captura tela (DXGI)  │         │ Autentica conexões   │     │ Renderiza stream(s)       │
 │ Codifica (JPEG/H264) │──TLS──▶ │ Registra por peer_id │◀TLS─│ Seleciona emissor (lista) │
 │ Injeta input         │ (saída) │ Roteia bytes         │(saída│ Captura mouse + teclado   │
 └──────────▲───────────┘         │ (nunca decodifica)   │     └─────────────┬────────────┘
            │                      └──────────────────────┘                   │
            └──────────────── INPUT_EVENT (peer_id = emissor alvo) ───────────┘
```

Cada mensagem carrega um `peer_id` (big-endian, 16 bytes no cabeçalho). O broker é um **cano burro autenticado**: nunca decodifica vídeo, apenas roteia. Adicionar um segundo emissor é só configuração — o código não muda.

### Protocolo de fio (resumo)

Envelope de 22 bytes fixos em toda mensagem:

| Offset | Bytes | Campo |
|--------|-------|-------|
| 0 | 1 | VERSION = `0x01` |
| 1 | 1 | TYPE (ver tabela) |
| 2 | 16 | PEER\_ID (UTF-8, padded com `\0`) |
| 18 | 4 | LENGTH (uint32 big-endian) |
| 22 | N | PAYLOAD |

Handshake: `HELLO → CHALLENGE (nonce 32B) → AUTH (HMAC-SHA256) → AUTH_OK`. Segredo nunca trafega. Heartbeat PING/PONG a cada 5s; sem PONG em 15s → conexão encerrada.

---

## Papéis

| Papel | `ROLE` no `.env` | Plataforma | Direção de conexão |
|-------|-----------------|------------|-------------------|
| Broker | `broker` | Linux (Node.js LTS) | escuta a `LISTEN_PORT` |
| Emissor | `sender` | Windows 10/11 | só saída ao broker |
| Receptor | `receiver` | Windows 10/11 | só saída ao broker |

O mesmo clone do repositório pode ser qualquer papel — basta rodar o bootstrap correspondente.

---

## Modelo de segurança

- **TLS em toda conexão** com CA própria de LAN (sem depender de CA pública). O broker apresenta `server-cert.pem`; os clientes pinam a CA via `TLS_CA_PATH`.
- **Autenticação desafio-resposta (HMAC-SHA256)** — `SHARED_SECRET` nunca trafega pelo fio.
- **Zero porta de entrada nas máquinas Windows** — somente conexões de saída ao broker.
- **RDP nativo (3389) desabilitado** como parte do setup.
- **Nada sensível versionado** — `.env`, chaves e certificados estão no `.gitignore`.

Este projeto pressupõe rede local confiável. Não foi auditado para exposição à internet pública; para esse cenário, use VPN ou túnel antes do broker.

Encontrou uma vulnerabilidade? Por favor reporte de forma responsável via [issue privada](https://github.com/Viniciusap/selfdesk/issues) antes de divulgar publicamente.

---

## Requisitos

| Componente | Requisito |
|-----------|-----------|
| Broker | Node.js LTS (Linux recomendado, mas qualquer SO com Node funciona) |
| Emissor / Receptor | Windows 10/11 com .NET 10 SDK |
| Bootstrap de certs | `openssl` no PATH (disponível na máquina do broker) |
| Rede | Todas as máquinas na mesma LAN |

Instalar .NET 10 no Windows:

```powershell
winget install Microsoft.DotNet.SDK.10
```

---

## Início rápido

```bash
git clone https://github.com/Viniciusap/selfdesk.git
cd selfdesk
```

### 1. Broker (Linux)

```bash
./scripts/bootstrap.sh broker
# Gera broker/.env, SHARED_SECRET e certificados em certs/.
# Anote o SHARED_SECRET impresso e copie certs/ca-cert.pem para as máquinas Windows.

sudo ufw allow from <SUA_SUBNET>/24 to any port <LISTEN_PORT> proto tcp
cd broker && npm install && npm start
```

### 2. Emissor — máquina a ser controlada (Windows)

```powershell
.\scripts\bootstrap.ps1 -Role sender
# Pergunta: host do broker, SHARED_SECRET, AGENT_ID, parâmetros de encode.

# Fechar RDP nativo (PowerShell como administrador):
Set-ItemProperty 'HKLM:\System\CurrentControlSet\Control\Terminal Server' fDenyTSConnections 1
Disable-NetFirewallRule -DisplayGroup "Remote Desktop"

cd agent && dotnet run
```

### 3. Receptor — máquina de controle (Windows)

```powershell
.\scripts\bootstrap.ps1 -Role receiver
# Pergunta: host do broker, SHARED_SECRET.

cd viewer && dotnet run
```

---

## Referência de configuração

Cada componente lê um `.env` no seu diretório (`broker/`, `agent/`, `viewer/`). Esses arquivos são **gerados pelo bootstrap** e **nunca commitados** — o repositório versiona apenas os `*.env.example`.

| Variável | broker | sender | receiver | Descrição |
|----------|:------:|:------:|:--------:|-----------|
| `ROLE` | ✓ | ✓ | ✓ | `broker` \| `sender` \| `receiver` |
| `SHARED_SECRET` | ✓ | ✓ | ✓ | Idêntico nas três máquinas; gerado no broker |
| `LISTEN_PORT` | ✓ | | | Porta TLS de escuta do broker |
| `ALLOWED_SENDERS` | ✓ | | | CSV de `AGENT_ID` permitidos (ex.: `laptop-01,laptop-02`) |
| `TLS_CERT_PATH` | ✓ | | | Caminho para `server-cert.pem` (broker) |
| `TLS_KEY_PATH` | ✓ | | | Caminho para `server-key.pem` (broker) |
| `LOG_LEVEL` | ✓ | | | `debug` \| `info` \| `warn` \| `error` |
| `AGENT_ID` | | ✓ | | Identificador único do emissor (deve estar em `ALLOWED_SENDERS`) |
| `BROKER_HOST` | | ✓ | ✓ | IP ou hostname do broker |
| `BROKER_PORT` | | ✓ | ✓ | Porta do broker |
| `TLS_CA_PATH` | | ✓ | ✓ | Caminho para `ca-cert.pem` (pinning) |
| `TARGET_FPS` | | ✓ | | FPS alvo de captura (padrão: `30`) |
| `ENCODER` | | ✓ | | `jpeg` \| `qsv` \| `nvenc` |
| `JPEG_QUALITY` | | ✓ | | Qualidade JPEG 1–100 (padrão: `75`) |

---

## Geração do .env (bootstrap)

Em vez de editar `.env` à mão, use os scripts em `scripts/`. Eles perguntam cada valor com defaults sensíveis e **nunca sobrescrevem** um `.env` existente sem confirmação.

**Linux / macOS:**
```bash
./scripts/bootstrap.sh broker     # gera broker/.env + SHARED_SECRET + certs/
./scripts/bootstrap.sh sender     # gera agent/.env
./scripts/bootstrap.sh receiver   # gera viewer/.env
```

**Windows:**
```powershell
.\scripts\bootstrap.ps1 -Role broker     # gera broker\.env (requer openssl no PATH)
.\scripts\bootstrap.ps1 -Role sender     # gera agent\.env
.\scripts\bootstrap.ps1 -Role receiver   # gera viewer\.env
```

O `SHARED_SECRET` é gerado uma vez no broker e deve ser o **mesmo** nos três `.env`. O bootstrap do broker o imprime ao final para você colar nas outras máquinas.

---

## Certificados TLS

A comunicação usa TLS com uma **CA própria de LAN** (sem CA pública). O bootstrap do broker gera:

- `certs/ca-cert.pem` — autoridade da LAN. **Copie para cada máquina emissor/receptor** (usada no pinning via `TLS_CA_PATH`).
- `certs/server-cert.pem` / `certs/server-key.pem` — par do servidor. A chave **nunca sai do broker**.

Toda a pasta `certs/` está no `.gitignore`. Chaves jamais vão para o repositório.

---

## Adicionar mais emissores

1. Na nova máquina, rode o bootstrap com role `sender` e escolha um `AGENT_ID` único (ex.: `laptop-02`).
2. Copie `certs/ca-cert.pem` do broker para a nova máquina.
3. No broker, acrescente o novo ID em `ALLOWED_SENDERS` (ex.: `laptop-01,laptop-02`) e reinicie.

Nenhuma mudança de código. O receptor exibe automaticamente o novo emissor na lista.

---

## Estrutura do projeto

```
selfdesk/
├── .github/workflows/ci.yml   # CI: build+testes broker (Ubuntu) + .NET (Windows)
├── scripts/
│   ├── bootstrap.sh           # Gera .env + certs (Linux/macOS)
│   └── bootstrap.ps1          # Gera .env + certs (Windows)
├── broker/                    # Node.js + TypeScript — relay autenticado
│   ├── src/
│   └── .env.example
├── agent/                     # C# / .NET 10 — emissor (captura + encode + inject)
│   ├── src/
│   └── .env.example
├── viewer/                    # C# / WPF / .NET 10 — receptor (render + input)
│   ├── src/
│   └── .env.example
├── shared/protocol/           # Constantes do protocolo espelhadas em TS e C#
├── selfdesk.sln               # Solution .NET (agent + viewer + testes)
├── LICENSE
└── README.md
```

---

## Roadmap

| Fase | Entrega | Estado |
|------|---------|--------|
| 0 | Esqueleto, TLS, autenticação HMAC, heartbeat, empacotamento open source | Em andamento |
| 1 | MVP: vídeo JPEG one-way + input de mouse | Pendente |
| 2 | Teclado + tuning de latência + medição de RTT | Pendente |
| 3 | Múltiplos emissores + seletor no viewer | Pendente |
| 4 | Codec de hardware (Quick Sync / NVENC) | Pendente |
| 5 | Agente como serviço Windows (tela de bloqueio / UAC) | Pendente |

---

## Contribuindo

Contribuições são bem-vindas. Fluxo:

1. Abra uma issue para discutir mudanças grandes antes de implementar.
2. Fork + branch descritiva (`feat/nome`, `fix/nome`).
3. Siga as convenções do projeto:
   - **Protocolo big-endian** em todo campo multibyte; `shared/protocol/` deve estar sincronizado entre TS e C#.
   - **Nenhum hardcode** de segredos, IPs ou portas — tudo via `.env`.
   - **DI + IOptions** nas apps .NET; sem singletons globais manuais.
   - **Logs estruturados** (`pino` no broker, `ILogger<T>` no .NET).
   - **TLS obrigatório** em toda conexão; sem fallback em texto puro.
   - **Interfaces para APIs Windows-only** (`IScreenCapturer`, `IInputInjector`) — testes unitários usam fakes.
   - **Conflação de vídeo:** Channel de capacidade 1; frame antigo é descartado. Nunca enfileirar.
4. `npm test` no broker deve estar verde. `dotnet test` na solution deve estar verde.
5. `git status` após build não deve mostrar `.env`, `.pem`, `.key`, `bin/`, `obj/` ou `node_modules/`.
6. Mensagens de commit em português, estilo convencional (ex.: `feat: roteamento por peer_id no broker`).
7. Abra o PR contra `main`.

---

## Uso responsável

SelfDesk é uma ferramenta de acesso remoto destinada exclusivamente a máquinas que **o próprio usuário possui ou tem autorização explícita para acessar**.

O uso desta ferramenta para **acesso não autorizado** a sistemas de terceiros, **vigilância não consentida**, ou qualquer forma de invasão de privacidade é expressamente proibido e constitui responsabilidade exclusiva do usuário, podendo configurar crime conforme a legislação aplicável (incluindo a Lei 12.737/2012 — "Lei Carolina Dieckmann" — no Brasil, e legislações equivalentes em outras jurisdições).

Os mantenedores deste projeto **não se responsabilizam** por qualquer uso indevido da ferramenta. Use com ética e respeito à privacidade alheia.

---

## Licença

Distribuído sob a licença MIT. Veja [LICENSE](LICENSE).
