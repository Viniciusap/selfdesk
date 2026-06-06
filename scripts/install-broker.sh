#!/usr/bin/env bash
#
# install-broker.sh — Universal broker installer.
# Detects Linux vs Windows (Git Bash / MSYS2 / Cygwin / WSL) and dispatches
# to the correct platform script automatically.
#
# One-liner (Linux, Git Bash, WSL):
#   curl -fsSL https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/install-broker.sh | bash
#
# Windows PowerShell (no bash):
#   iwr https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/update-broker.ps1 | iex

set -euo pipefail

BASE_URL="https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts"

case "$(uname -s 2>/dev/null)" in
    Linux*)
        exec bash <(curl -fsSL "$BASE_URL/update-broker.sh")
        ;;
    MINGW*|MSYS*|CYGWIN*)
        echo "Windows detected (Git Bash/MSYS2) — delegating to PowerShell..."
        powershell.exe -ExecutionPolicy Bypass -Command \
            "& { iwr '$BASE_URL/update-broker.ps1' -UseBasicParsing | iex }"
        ;;
    *)
        echo "Platform '$(uname -s)' not recognized."
        echo ""
        echo "  Linux / WSL:"
        echo "    curl -fsSL $BASE_URL/update-broker.sh | bash"
        echo ""
        echo "  Windows PowerShell:"
        echo "    iwr $BASE_URL/update-broker.ps1 | iex"
        exit 1
        ;;
esac
