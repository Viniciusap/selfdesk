#!/usr/bin/env bash
#
# update-broker.sh — atualiza o SelfDesk Broker a partir da latest release do GitHub.
#
# Uso (rodar no servidor como vini):
#   bash update-broker.sh
#
# One-liner:
#   curl -fsSL https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/update-broker.sh | bash
#
# Variáveis de ambiente opcionais:
#   INSTALL_DIR   Diretório de instalação. Padrão: ~/selfdesk
#

set -euo pipefail

# ── Banner ────────────────────────────────────────────────────────────────────
printf '\033[36m'
cat << 'BANNER'
  ____       _  __ ____            _
 / ___|  ___| |/ _|  _ \  ___  ___| | __
 \___ \ / _ \ | |_| | | |/ _ \/ __| |/ /
  ___) |  __/ |  _| |_| |  __/\__ \   <
 |____/ \___|_|_||____/ \___||___/_|\_\
BANNER
printf '\033[36m  Broker Updater  —  github.com/Viniciusap/selfdesk\033[0m\n\n'
# ─────────────────────────────────────────────────────────────────────────────

REPO_OWNER="Viniciusap"
REPO_NAME="selfdesk"
ASSET_NAME="selfdesk-broker-linux-x64.tar.gz"
INSTALL_DIR="${INSTALL_DIR:-$HOME/selfdesk}"
DOWNLOAD_URL="https://github.com/$REPO_OWNER/$REPO_NAME/releases/latest/download/$ASSET_NAME"

TMP_TARBALL="/tmp/selfdesk-broker-update-$$.tar.gz"
TMP_DIR="/tmp/selfdesk-broker-update-$$"

step() { echo ""; echo "→ $*"; }
ok()   { echo "  ✔ $*"; }
warn() { echo "  ⚠ $*"; }

# ── 1. Baixar zip ─────────────────────────────────────────────────────────────

step "Baixando $ASSET_NAME..."
echo "  URL: $DOWNLOAD_URL"

if command -v curl &>/dev/null; then
    curl -fsSL "$DOWNLOAD_URL" -o "$TMP_TARBALL"
elif command -v wget &>/dev/null; then
    wget -q "$DOWNLOAD_URL" -O "$TMP_TARBALL"
else
    echo "ERRO: curl ou wget necessário." >&2
    exit 1
fi
ok "Download concluído."

# ── 2. Extrair ────────────────────────────────────────────────────────────────

step "Extraindo..."
rm -rf "$TMP_DIR"
mkdir -p "$TMP_DIR"
tar -xzf "$TMP_TARBALL" -C "$TMP_DIR"
rm -f "$TMP_TARBALL"
ok "Extraído em $TMP_DIR"

# ── 3. Parar broker ───────────────────────────────────────────────────────────

step "Parando broker..."
if pkill -f "node dist/index.js" 2>/dev/null; then
    sleep 1
    ok "Broker parado."
else
    warn "Broker não estava rodando."
fi

# ── 4. Atualizar dist/ ────────────────────────────────────────────────────────

step "Atualizando $INSTALL_DIR ..."
mkdir -p "$INSTALL_DIR"
cp -r "$TMP_DIR/selfdesk-broker/dist"         "$INSTALL_DIR/"
cp -r "$TMP_DIR/selfdesk-broker/node_modules" "$INSTALL_DIR/"
cp    "$TMP_DIR/selfdesk-broker/package.json"  "$INSTALL_DIR/"
rm -rf "$TMP_DIR"
ok "Arquivos atualizados."

# ── 5. Reiniciar broker ───────────────────────────────────────────────────────

step "Iniciando broker..."
cd "$INSTALL_DIR"
nohup node dist/index.js >> broker.log 2>&1 &
BROKER_PID=$!
sleep 1

if kill -0 "$BROKER_PID" 2>/dev/null; then
    ok "Broker rodando (PID $BROKER_PID)."
else
    echo "ERRO: broker não iniciou. Verifique $INSTALL_DIR/broker.log" >&2
    exit 1
fi

echo ""
echo "=== Update broker concluído ==="
echo "  Diretório : $INSTALL_DIR"
echo "  Log       : $INSTALL_DIR/broker.log"
echo ""
echo "Para acompanhar:"
echo "  tail -f $INSTALL_DIR/broker.log"
