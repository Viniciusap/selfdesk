#!/usr/bin/env bash
#
# setup.sh — One-command SelfDesk setup (issue #2). Picks a role, installs the
# matching release build, and generates its .env via bootstrap.
#
# One-liner:
#   curl -fsSL https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/setup.sh | bash
#
# Non-interactive:
#   SELFDESK_ROLE=broker curl -fsSL .../setup.sh | bash
#
# This is a thin orchestrator over the existing per-role scripts — it does not
# duplicate their logic. On Linux/WSL it downloads and runs install-broker.sh,
# then the bootstrap.sh that step bundles into the install directory (senders
# and viewers are Windows-only .NET/WPF apps — see the delegation branch
# below). On Windows (Git Bash/MSYS2/Cygwin) it delegates entirely to
# setup.ps1, which offers all three roles.
#
# For fine-grained control (custom install dir, systemd unit, etc.) run the
# per-role scripts directly instead — see README.md "Install / Update".

set -euo pipefail

BASE_URL="https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts"

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
printf '\033[36m  Setup  —  github.com/Viniciusap/selfdesk\033[0m\n\n'

case "$(uname -s 2>/dev/null)" in
    Linux*)
        ROLE="${SELFDESK_ROLE:-}"
        if [ -z "$ROLE" ]; then
            echo "Which role is this machine?"
            echo "  [1] broker  — routes video/input between senders and viewers"
            echo "  [2] sender  — the machine being controlled (Windows only)"
            echo "  [3] viewer  — the control machine (Windows only)"
            echo ""
            # setup.sh's own stdin is the curl|bash pipe itself (already consumed
            # by bash reading the script) — read from the controlling terminal
            # directly, same fix every curl-piped interactive installer needs.
            read -rp "Enter 1, 2, or 3: " choice < /dev/tty
            case "$choice" in
                1) ROLE="broker" ;;
                2) ROLE="sender" ;;
                3) ROLE="viewer" ;;
                *) echo "ERROR: invalid choice '$choice'" >&2; exit 1 ;;
            esac
        fi

        if [ "$ROLE" != "broker" ]; then
            echo ""
            echo "  $ROLE is a Windows-only .NET/WPF app — this machine is Linux."
            echo "  On the corresponding Windows machine, run:"
            echo "    irm https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/setup.ps1 | iex"
            exit 0
        fi

        INSTALL_DIR="${INSTALL_DIR:-$HOME/selfdesk}"

        echo "-> Role: broker"
        echo "-> Downloading and running install-broker.sh..."
        curl -fsSL "$BASE_URL/install-broker.sh" | INSTALL_DIR="$INSTALL_DIR" bash

        BOOTSTRAP="$INSTALL_DIR/scripts/bootstrap.sh"
        ENV_FILE="$INSTALL_DIR/.env"

        if [ ! -f "$ENV_FILE" ]; then
            if [ ! -f "$BOOTSTRAP" ]; then
                echo "ERROR: bootstrap.sh not found at $BOOTSTRAP — install step may have failed. Aborting setup." >&2
                exit 1
            fi
            echo ""
            echo "-> Running bootstrap (Role=broker)..."
            # bootstrap.sh prompts interactively (read -rp) — redirect its stdin
            # from the terminal explicitly, since setup.sh's own stdin here is
            # still the exhausted curl|bash pipe (see the /dev/tty note above).
            "$BOOTSTRAP" broker < /dev/tty
        else
            echo ""
            echo ".env already exists at $ENV_FILE — skipping bootstrap (existing config kept)."
        fi

        echo ""
        echo "=== Setup complete ==="
        echo "   Role      : broker"
        echo "   Directory : $INSTALL_DIR"
        echo ""
        echo "Start the broker:"
        echo "  cd $INSTALL_DIR && node dist/index.js"
        ;;

    MINGW*|MSYS*|CYGWIN*)
        printf '\033[0m'
        echo "Windows detected (Git Bash/MSYS2) — delegating to PowerShell..."
        powershell.exe -ExecutionPolicy Bypass -Command \
            "& { irm '$BASE_URL/setup.ps1' | iex }"
        ;;

    *)
        printf '\033[0m'
        echo "Platform '$(uname -s)' not recognized."
        echo ""
        echo "  Linux / WSL:"
        echo "    curl -fsSL $BASE_URL/setup.sh | bash"
        echo ""
        echo "  Windows PowerShell:"
        echo "    irm $BASE_URL/setup.ps1 | iex"
        exit 1
        ;;
esac
