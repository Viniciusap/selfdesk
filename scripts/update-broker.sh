#!/usr/bin/env bash
#
# update-broker.sh — Updates the SelfDesk Broker from the latest GitHub release.
#
# Usage (run on the server):
#   bash update-broker.sh
#
# One-liner:
#   curl -fsSL https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/install-broker.sh | bash
#
# Optional environment variables:
#   INSTALL_DIR   Installation directory. Default: ~/selfdesk
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

step() { echo ""; echo "-> $*"; }
ok()   { echo "  ✔ $*"; }
warn() { echo "  ⚠ $*"; }

# ── 1. Download ───────────────────────────────────────────────────────────────

step "Downloading $ASSET_NAME..."
echo "  URL: $DOWNLOAD_URL"

if command -v curl &>/dev/null; then
    curl -fsSL "$DOWNLOAD_URL" -o "$TMP_TARBALL"
elif command -v wget &>/dev/null; then
    wget -q "$DOWNLOAD_URL" -O "$TMP_TARBALL"
else
    echo "ERROR: curl or wget is required." >&2
    exit 1
fi
ok "Download complete."

# ── 2. Extract ────────────────────────────────────────────────────────────────

step "Extracting..."
rm -rf "$TMP_DIR"
mkdir -p "$TMP_DIR"
tar -xzf "$TMP_TARBALL" -C "$TMP_DIR"
rm -f "$TMP_TARBALL"
ok "Extracted to $TMP_DIR"

# ── 3. Stop broker ────────────────────────────────────────────────────────────

step "Stopping broker..."
if pkill -f "node dist/index.js" 2>/dev/null; then
    sleep 1
    ok "Broker stopped."
else
    warn "Broker was not running."
fi

# ── 4. Update files ───────────────────────────────────────────────────────────

step "Updating $INSTALL_DIR ..."
mkdir -p "$INSTALL_DIR"
cp -r "$TMP_DIR/selfdesk-broker/dist"         "$INSTALL_DIR/"
cp -r "$TMP_DIR/selfdesk-broker/node_modules" "$INSTALL_DIR/"
cp    "$TMP_DIR/selfdesk-broker/package.json"  "$INSTALL_DIR/"
rm -rf "$TMP_DIR"
ok "Files updated."

# ── 5. Start broker ───────────────────────────────────────────────────────────

step "Starting broker..."
cd "$INSTALL_DIR"
nohup node dist/index.js >> broker.log 2>&1 &
BROKER_PID=$!
sleep 1

if kill -0 "$BROKER_PID" 2>/dev/null; then
    ok "Broker running (PID $BROKER_PID)."
else
    echo "ERROR: broker did not start. Check $INSTALL_DIR/broker.log" >&2
    exit 1
fi

echo ""
echo "=== Broker update complete ==="
echo "  Directory : $INSTALL_DIR"
echo "  Log       : $INSTALL_DIR/broker.log"
echo ""
echo "To follow logs:"
echo "  tail -f $INSTALL_DIR/broker.log"
