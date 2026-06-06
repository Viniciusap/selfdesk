#!/usr/bin/env bash
#
# bootstrap.sh — Generates the local .env for a component (and TLS certs for the broker).
#
# Usage:  ./scripts/bootstrap.sh <broker|sender|receiver>
#
# Nothing generated here should be committed: .gitignore ignores .env and certs/.
# Run this script BEFORE `npm start` / `dotnet run`.
#
set -euo pipefail

ROLE="${1:-}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CERT_DIR="$ROOT/certs"

usage() {
  echo "Usage: $0 <broker|sender|receiver>"
  exit 1
}
[ -z "$ROLE" ] && usage

# prompt VAR "Question" "default"
prompt() {
  local __var="$1" q="$2" def="${3:-}" ans
  if [ -n "$def" ]; then
    read -rp "$q [$def]: " ans
    ans="${ans:-$def}"
  else
    read -rp "$q: " ans
    while [ -z "$ans" ]; do read -rp "$q (required): " ans; done
  fi
  printf -v "$__var" '%s' "$ans"
}

confirm_overwrite() {
  if [ -f "$1" ]; then
    read -rp ".env already exists at $1. Overwrite? (y/N): " a
    [[ "$a" == "y" || "$a" == "Y" ]] || { echo "Aborted."; exit 0; }
  fi
}

gen_secret() { openssl rand -base64 48 2>/dev/null | tr -d '\n'; }

gen_certs() {
  mkdir -p "$CERT_DIR"
  if [ -f "$CERT_DIR/server-cert.pem" ]; then
    echo "Certificates already exist in certs/ — keeping them."
    return
  fi
  local ip; prompt ip "IP/hostname of this broker (used in the certificate SAN)" "$(hostname -I 2>/dev/null | awk '{print $1}')"
  echo "Generating CA and server certificate..."
  openssl req -x509 -newkey rsa:4096 -nodes \
    -keyout "$CERT_DIR/ca-key.pem" -out "$CERT_DIR/ca-cert.pem" \
    -days 730 -subj "/CN=selfdesk-lan-ca" >/dev/null 2>&1
  openssl req -newkey rsa:4096 -nodes \
    -keyout "$CERT_DIR/server-key.pem" -out "$CERT_DIR/server.csr" \
    -subj "/CN=$ip" >/dev/null 2>&1
  openssl x509 -req -in "$CERT_DIR/server.csr" \
    -CA "$CERT_DIR/ca-cert.pem" -CAkey "$CERT_DIR/ca-key.pem" -CAcreateserial \
    -out "$CERT_DIR/server-cert.pem" -days 825 \
    -extfile <(printf "subjectAltName=IP:%s" "$ip") >/dev/null 2>&1
  rm -f "$CERT_DIR/server.csr"
  echo "Done. Distribute certs/ca-cert.pem to sender/receiver machines (TLS pinning)."
}

case "$ROLE" in
  broker)
    OUT="$ROOT/broker/.env"
    confirm_overwrite "$OUT"
    prompt LISTEN_PORT      "Broker listen port" "7000"
    prompt ALLOWED_SENDERS  "Allowed sender IDs (comma-separated)" "laptop-01"
    gen_certs
    SECRET="$(gen_secret)"
    mkdir -p "$ROOT/broker"
    cat > "$OUT" <<EOF
ROLE=broker
SHARED_SECRET=$SECRET
LISTEN_PORT=$LISTEN_PORT
ALLOWED_SENDERS=$ALLOWED_SENDERS
TLS_CERT_PATH=../certs/server-cert.pem
TLS_KEY_PATH=../certs/server-key.pem
LOG_LEVEL=info
EOF
    echo "Generated: $OUT"
    echo
    echo "==> SHARED_SECRET (paste into sender and receiver .env files):"
    echo "    $SECRET"
    ;;

  sender)
    OUT="$ROOT/sender/.env"
    confirm_overwrite "$OUT"
    prompt SENDER_ID      "Unique ID for this sender" "laptop-01"
    prompt BROKER_HOST    "Broker IP/hostname" ""
    prompt BROKER_PORT    "Broker port" "7000"
    prompt SHARED_SECRET  "SHARED_SECRET (same as the broker)" ""
    if [ ${#SHARED_SECRET} -lt 32 ]; then
      echo "  Warning: SHARED_SECRET looks short (${#SHARED_SECRET} chars). Make sure you copied the full value from the broker."
      read -rp "  Continue anyway? (y/N): " _confirm
      [[ "$_confirm" == "y" || "$_confirm" == "Y" ]] || { echo "Aborted."; exit 1; }
    fi
    prompt TARGET_FPS    "Target FPS" "30"
    prompt ENCODER       "Encoder (jpeg|qsv|nvenc)" "jpeg"
    prompt JPEG_QUALITY  "JPEG quality (1-100)" "75"
    mkdir -p "$ROOT/sender"
    cat > "$OUT" <<EOF
ROLE=sender
SENDER_ID=$SENDER_ID
SHARED_SECRET=$SHARED_SECRET
BROKER_HOST=$BROKER_HOST
BROKER_PORT=$BROKER_PORT
TLS_CA_PATH=../certs/ca-cert.pem
TARGET_FPS=$TARGET_FPS
ENCODER=$ENCODER
JPEG_QUALITY=$JPEG_QUALITY
EOF
    echo "Generated: $OUT"
    echo "Remember to copy certs/ca-cert.pem from the broker to this machine."
    ;;

  receiver)
    OUT="$ROOT/viewer/.env"
    confirm_overwrite "$OUT"
    prompt BROKER_HOST    "Broker IP/hostname" ""
    prompt BROKER_PORT    "Broker port" "7000"
    prompt SHARED_SECRET  "SHARED_SECRET (same as the broker)" ""
    if [ ${#SHARED_SECRET} -lt 32 ]; then
      echo "  Warning: SHARED_SECRET looks short (${#SHARED_SECRET} chars). Make sure you copied the full value from the broker."
      read -rp "  Continue anyway? (y/N): " _confirm
      [[ "$_confirm" == "y" || "$_confirm" == "Y" ]] || { echo "Aborted."; exit 1; }
    fi
    mkdir -p "$ROOT/viewer"
    cat > "$OUT" <<EOF
ROLE=receiver
SHARED_SECRET=$SHARED_SECRET
BROKER_HOST=$BROKER_HOST
BROKER_PORT=$BROKER_PORT
TLS_CA_PATH=../certs/ca-cert.pem
EOF
    echo "Generated: $OUT"
    echo "Remember to copy certs/ca-cert.pem from the broker to this machine."
    ;;

  *) usage ;;
esac
