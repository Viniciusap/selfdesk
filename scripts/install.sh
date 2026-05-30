#!/usr/bin/env bash
#
# install.sh — instala dependências e compila o broker SelfDesk (Linux).
#
# Uso:  ./scripts/install.sh <broker|sender|receiver>
#
# Pré-requisitos ausentes (Node.js LTS) são instalados automaticamente
# via apt-get / NodeSource.
#
# Após este script, rode:
#   ./scripts/bootstrap.sh broker
#   cd broker && npm start
#
# Nota: sender e receiver têm TFM net10.0-windows e só compilam no Windows.
# Nesse caso use scripts/install.ps1 na máquina Windows correspondente.
#
set -euo pipefail

ROLE="${1:-}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

usage() {
  echo "Uso: $0 <broker|sender|receiver>"
  exit 1
}
[ -z "$ROLE" ] && usage

# ── Helpers ──────────────────────────────────────────────────────────────────

# Verifica se o comando existe; se não, instala o pacote apt indicado.
# Para node, detecta versão e usa NodeSource se < 20.
ensure_node() {
  if command -v node &>/dev/null; then
    local ver
    ver=$(node --version | sed 's/v//' | cut -d. -f1)
    if [ "$ver" -ge 20 ]; then
      echo "  Node.js v$(node --version) já instalado."
      return
    fi
    echo "  Node.js encontrado mas versão < 20 ($(node --version)). Atualizando via NodeSource..."
  else
    echo "  Node.js não encontrado. Instalando via NodeSource (LTS)..."
  fi

  if ! command -v curl &>/dev/null; then
    sudo apt-get update -qq && sudo apt-get install -y curl
  fi
  curl -fsSL https://deb.nodesource.com/setup_lts.x | sudo -E bash -
  sudo apt-get install -y nodejs
  echo "  Node.js $(node --version) instalado."
}

ensure_cmd() {
  local bin="$1" pkg="$2"
  if command -v "$bin" &>/dev/null; then
    echo "  $bin já disponível."
    return
  fi
  echo "  $bin não encontrado. Instalando $pkg..."
  sudo apt-get update -qq && sudo apt-get install -y "$pkg"
}

# ── Instalação por papel ──────────────────────────────────────────────────────

case "$ROLE" in
  broker)
    echo ''
    echo '=== SelfDesk Install — broker ==='
    echo ''
    echo '→ Verificando Node.js LTS...'
    ensure_node

    echo '→ Verificando openssl...'
    ensure_cmd openssl openssl

    echo '→ Instalando dependências npm...'
    cd "$ROOT/broker"
    npm install

    echo '→ Compilando TypeScript...'
    npm run build
    cd "$ROOT"

    echo ''
    echo '✔ Broker compilado em broker/dist/'
    echo ''
    echo 'Próximo passo:'
    echo '  ./scripts/bootstrap.sh broker'
    echo '  cd broker && npm start'
    ;;

  sender|receiver)
    echo ''
    echo "=== SelfDesk Install — $ROLE ==="
    echo ''
    echo "  Os componentes agent (sender) e viewer (receiver) usam o"
    echo "  target framework net10.0-windows e só compilam no Windows."
    echo ''
    echo '  Na máquina Windows correspondente, rode:'
    echo "    .\\scripts\\install.ps1 -Role $ROLE"
    echo ''
    echo '  Saindo sem alterações.'
    exit 0
    ;;

  *)
    usage
    ;;
esac
