#!/usr/bin/env bash
#
# install.sh — Installs dependencies and compiles the SelfDesk broker (Linux).
#
# Usage:  ./scripts/install.sh <broker|sender|receiver>
#
# Missing prerequisites (Node.js LTS) are installed automatically
# via apt-get / NodeSource.
#
# After this script, run:
#   ./scripts/bootstrap.sh broker
#   cd broker && npm start
#
# Note: sender and receiver use the net10.0-windows TFM and only build on Windows.
# In that case use scripts/install.ps1 on the corresponding Windows machine.
#
set -euo pipefail

ROLE="${1:-}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

usage() {
  echo "Usage: $0 <broker|sender|receiver>"
  exit 1
}
[ -z "$ROLE" ] && usage

# ── Helpers ──────────────────────────────────────────────────────────────────

ensure_node() {
  if command -v node &>/dev/null; then
    local ver
    ver=$(node --version | sed 's/v//' | cut -d. -f1)
    if [ "$ver" -ge 20 ]; then
      echo "  Node.js v$(node --version) already installed."
      return
    fi
    echo "  Node.js found but version < 20 ($(node --version)). Upgrading via NodeSource..."
  else
    echo "  Node.js not found. Installing via NodeSource (LTS)..."
  fi

  if ! command -v curl &>/dev/null; then
    sudo apt-get update -qq && sudo apt-get install -y curl
  fi
  curl -fsSL https://deb.nodesource.com/setup_lts.x | sudo -E bash -
  sudo apt-get install -y nodejs
  echo "  Node.js $(node --version) installed."
}

ensure_cmd() {
  local bin="$1" pkg="$2"
  if command -v "$bin" &>/dev/null; then
    echo "  $bin already available."
    return
  fi
  echo "  $bin not found. Installing $pkg..."
  sudo apt-get update -qq && sudo apt-get install -y "$pkg"
}

# ── Install by role ───────────────────────────────────────────────────────────

case "$ROLE" in
  broker)
    echo ''
    echo '=== SelfDesk Install — broker ==='
    echo ''
    echo '-> Checking Node.js LTS...'
    ensure_node

    echo '-> Checking openssl...'
    ensure_cmd openssl openssl

    echo '-> Installing npm dependencies...'
    cd "$ROOT/broker"
    npm install

    echo '-> Compiling TypeScript...'
    npm run build
    cd "$ROOT"

    echo ''
    echo '✔ Broker compiled to broker/dist/'
    echo ''

    read -rp '-> Install as a systemd service? (y/N): ' _systemd
    if [[ "$_systemd" == "y" || "$_systemd" == "Y" ]]; then
      NODE_BIN="$(command -v node)"
      UNIT_FILE='/etc/systemd/system/selfdesk-broker.service'
      sudo tee "$UNIT_FILE" > /dev/null <<EOF
[Unit]
Description=SelfDesk Broker
After=network.target

[Service]
Type=simple
WorkingDirectory=$ROOT/broker
ExecStart=$NODE_BIN dist/index.js
Restart=always
RestartSec=5
EnvironmentFile=$ROOT/broker/.env

[Install]
WantedBy=multi-user.target
EOF
      sudo systemctl daemon-reload
      sudo systemctl enable selfdesk-broker
      echo '✔ Service selfdesk-broker installed and enabled.'
      echo '  (Run bootstrap.sh broker before starting: systemctl start selfdesk-broker)'
    fi

    echo ''
    echo 'Next step:'
    echo '  ./scripts/bootstrap.sh broker'
    if [[ "$_systemd" == "y" || "$_systemd" == "Y" ]]; then
      echo '  sudo systemctl start selfdesk-broker'
    else
      echo '  cd broker && npm start'
    fi
    ;;

  sender|receiver)
    echo ''
    echo "=== SelfDesk Install — $ROLE ==="
    echo ''
    echo "  The sender and receiver components use the net10.0-windows"
    echo "  target framework and only build on Windows."
    echo ''
    echo '  On the corresponding Windows machine, run:'
    echo "    .\\scripts\\install.ps1 -Role $ROLE"
    echo ''
    echo '  Exiting without changes.'
    exit 0
    ;;

  *)
    usage
    ;;
esac
