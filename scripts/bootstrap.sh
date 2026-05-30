#!/usr/bin/env bash
#
# bootstrap.sh — gera o .env local de um componente (e os certificados, no broker).
#
# Uso:  ./scripts/bootstrap.sh <broker|sender|receiver>
#
# Nada gerado aqui deve ser commitado: o .gitignore ignora .env e certs/.
# Rode este script ANTES de `npm start` / `dotnet run`.
#
set -euo pipefail

ROLE="${1:-}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CERT_DIR="$ROOT/certs"

usage() {
  echo "Uso: $0 <broker|sender|receiver>"
  exit 1
}
[ -z "$ROLE" ] && usage

# prompt VAR "Pergunta" "default"
prompt() {
  local __var="$1" q="$2" def="${3:-}" ans
  if [ -n "$def" ]; then
    read -rp "$q [$def]: " ans
    ans="${ans:-$def}"
  else
    read -rp "$q: " ans
    while [ -z "$ans" ]; do read -rp "$q (obrigatório): " ans; done
  fi
  printf -v "$__var" '%s' "$ans"
}

confirm_overwrite() {
  if [ -f "$1" ]; then
    read -rp ".env já existe em $1. Sobrescrever? (y/N): " a
    [[ "$a" == "y" || "$a" == "Y" ]] || { echo "Abortado."; exit 0; }
  fi
}

gen_secret() { openssl rand -base64 48 2>/dev/null | tr -d '\n'; }

gen_certs() {
  mkdir -p "$CERT_DIR"
  if [ -f "$CERT_DIR/server-cert.pem" ]; then
    echo "Certificados já existem em certs/ — mantendo."
    return
  fi
  local ip; prompt ip "IP/host deste broker (vai no SAN do certificado)" "$(hostname -I 2>/dev/null | awk '{print $1}')"
  echo "Gerando CA e certificado de servidor..."
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
  echo "OK. Distribua certs/ca-cert.pem para as máquinas sender/receiver (pinning)."
}

case "$ROLE" in
  broker)
    OUT="$ROOT/broker/.env"
    confirm_overwrite "$OUT"
    prompt LISTEN_PORT "Porta de escuta do broker" "7000"
    prompt ALLOWED_SENDERS "IDs de emissores permitidos (CSV)" "laptop-01"
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
    echo "Gerado: $OUT"
    echo
    echo "==> SHARED_SECRET (cole nos .env de sender e receiver):"
    echo "    $SECRET"
    ;;

  sender)
    OUT="$ROOT/agent/.env"
    confirm_overwrite "$OUT"
    prompt AGENT_ID     "ID único deste emissor" "laptop-01"
    prompt BROKER_HOST  "IP/host do broker" ""
    prompt BROKER_PORT  "Porta do broker" "7000"
    prompt SHARED_SECRET "SHARED_SECRET (idêntico ao do broker)" ""
    if [ ${#SHARED_SECRET} -lt 32 ]; then
      echo "  Aviso: SHARED_SECRET parece curto (${#SHARED_SECRET} chars). Certifique-se de copiar o valor completo gerado pelo broker."
      read -rp "  Continuar mesmo assim? (y/N): " _confirm
      [[ "$_confirm" == "y" || "$_confirm" == "Y" ]] || { echo "Abortado."; exit 1; }
    fi
    prompt TARGET_FPS   "FPS alvo" "30"
    prompt ENCODER      "Encoder (jpeg|qsv|nvenc)" "jpeg"
    prompt JPEG_QUALITY "Qualidade JPEG (1-100)" "75"
    mkdir -p "$ROOT/agent"
    cat > "$OUT" <<EOF
ROLE=sender
AGENT_ID=$AGENT_ID
SHARED_SECRET=$SHARED_SECRET
BROKER_HOST=$BROKER_HOST
BROKER_PORT=$BROKER_PORT
TLS_CA_PATH=../certs/ca-cert.pem
TARGET_FPS=$TARGET_FPS
ENCODER=$ENCODER
JPEG_QUALITY=$JPEG_QUALITY
EOF
    echo "Gerado: $OUT"
    echo "Lembre-se de copiar certs/ca-cert.pem do broker para esta máquina."
    ;;

  receiver)
    OUT="$ROOT/viewer/.env"
    confirm_overwrite "$OUT"
    prompt BROKER_HOST  "IP/host do broker" ""
    prompt BROKER_PORT  "Porta do broker" "7000"
    prompt SHARED_SECRET "SHARED_SECRET (idêntico ao do broker)" ""
    if [ ${#SHARED_SECRET} -lt 32 ]; then
      echo "  Aviso: SHARED_SECRET parece curto (${#SHARED_SECRET} chars). Certifique-se de copiar o valor completo gerado pelo broker."
      read -rp "  Continuar mesmo assim? (y/N): " _confirm
      [[ "$_confirm" == "y" || "$_confirm" == "Y" ]] || { echo "Abortado."; exit 1; }
    fi
    mkdir -p "$ROOT/viewer"
    cat > "$OUT" <<EOF
ROLE=receiver
SHARED_SECRET=$SHARED_SECRET
BROKER_HOST=$BROKER_HOST
BROKER_PORT=$BROKER_PORT
TLS_CA_PATH=../certs/ca-cert.pem
EOF
    echo "Gerado: $OUT"
    echo "Lembre-se de copiar certs/ca-cert.pem do broker para esta máquina."
    ;;

  *) usage ;;
esac
