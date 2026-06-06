#!/usr/bin/env bash
#
# install-broker.sh — Install or update the SelfDesk Broker.
# Detects Linux vs Windows (Git Bash / MSYS2 / Cygwin / WSL) and dispatches
# to the correct platform implementation automatically.
#
# One-liner (Linux, Git Bash, WSL):
#   curl -fsSL https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/install-broker.sh | bash
#
# Windows PowerShell (no bash):
#   irm https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/install-broker.ps1 | iex
#
# Optional environment variable:
#   INSTALL_DIR   Installation directory. Default: ~/selfdesk

set -euo pipefail

BASE_URL="https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts"

# ── Banner ────────────────────────────────────────────────────────────────────
printf '\033[36m'
cat << 'BANNER'
 /-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\
|                                                         |
\  ___           _      ___  ___                 _        /
- (  _`\        (_ )  /'___)(  _`\              ( )       -
/ | (_(_)   __   | | | (__  | | ) |   __    ___ | |/')    \
| `\__ \  /'__`\ | | | ,__) | | | ) /'__`\/',__)| , <     |
\ ( )_) |(  ___/ | | | |    | |_) |(  ___/\__, \| |\`\    /
- `\____)`\____)(___)(_)    (____/'`\____)(____/(_) (_)   -
/                                                         \
|                                                         |
\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/
BANNER
printf '\033[36m  Broker Installer  —  github.com/Viniciusap/selfdesk\033[0m\n\n'
# ─────────────────────────────────────────────────────────────────────────────

case "$(uname -s 2>/dev/null)" in
    Linux*)
        # ── Linux / WSL ───────────────────────────────────────────────────────

        REPO_OWNER="Viniciusap"
        REPO_NAME="selfdesk"
        ASSET_NAME="selfdesk-broker-linux-x64.tar.gz"
        INSTALL_DIR="${INSTALL_DIR:-$HOME/selfdesk}"
        DOWNLOAD_URL="https://github.com/$REPO_OWNER/$REPO_NAME/releases/latest/download/$ASSET_NAME"

        TMP_TARBALL="/tmp/selfdesk-broker-install-$$.tar.gz"
        TMP_DIR="/tmp/selfdesk-broker-install-$$"

        step() { echo ""; echo "-> $*"; }
        ok()   { echo "  ✔ $*"; }
        warn() { echo "  ⚠ $*"; }

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

        step "Extracting..."
        rm -rf "$TMP_DIR"
        mkdir -p "$TMP_DIR"
        tar -xzf "$TMP_TARBALL" -C "$TMP_DIR"
        rm -f "$TMP_TARBALL"
        ok "Extracted to $TMP_DIR"

        step "Stopping broker..."
        if pkill -f "node dist/index.js" 2>/dev/null; then
            sleep 1
            ok "Broker stopped."
        else
            warn "Broker was not running."
        fi

        step "Updating $INSTALL_DIR ..."
        mkdir -p "$INSTALL_DIR"

        # Copy everything from the release except existing .env and certs/
        SRC="$TMP_DIR/selfdesk-broker"
        for item in "$SRC"/* "$SRC"/.[!.]*; do
            [ -e "$item" ] || continue
            name="$(basename "$item")"
            [[ "$name" == ".env" || "$name" == "certs" ]] && continue
            cp -r "$item" "$INSTALL_DIR/"
        done
        rm -rf "$TMP_DIR"
        ok "Files updated."

        ENV_FILE="$INSTALL_DIR/.env"
        if [ ! -f "$ENV_FILE" ]; then
            echo ""
            echo "=== Broker files installed to $INSTALL_DIR ==="
            echo ""
            echo "Next step — generate .env, SHARED_SECRET, and TLS certificates:"
            echo "  cd $INSTALL_DIR && ./scripts/bootstrap.sh broker"
            echo ""
            echo "Then open the firewall and start:"
            echo "  sudo ufw allow from <YOUR_SUBNET>/24 to any port <LISTEN_PORT> proto tcp"
            echo "  node dist/index.js"
        else
            step "Starting broker..."
            cd "$INSTALL_DIR"
            nohup node dist/index.js >> broker.log 2>&1 &
            BROKER_PID=$!
            sleep 1

            if kill -0 "$BROKER_PID" 2>/dev/null; then
                ok "Broker running (PID $BROKER_PID)."
                echo ""
                echo "=== Broker update complete ==="
                echo "  Directory : $INSTALL_DIR"
                echo "  Log       : $INSTALL_DIR/broker.log"
                echo ""
                echo "To follow logs:"
                echo "  tail -f $INSTALL_DIR/broker.log"
            else
                echo "ERROR: broker did not start. Check $INSTALL_DIR/broker.log" >&2
                exit 1
            fi
        fi
        ;;

    MINGW*|MSYS*|CYGWIN*)
        printf '\033[0m'
        echo "Windows detected (Git Bash/MSYS2) — delegating to PowerShell..."
        powershell.exe -ExecutionPolicy Bypass -Command \
            "& { irm '$BASE_URL/install-broker.ps1' | iex }"
        ;;

    *)
        printf '\033[0m'
        echo "Platform '$(uname -s)' not recognized."
        echo ""
        echo "  Linux / WSL:"
        echo "    curl -fsSL $BASE_URL/install-broker.sh | bash"
        echo ""
        echo "  Windows PowerShell:"
        echo "    irm $BASE_URL/install-broker.ps1 | iex"
        exit 1
        ;;
esac
