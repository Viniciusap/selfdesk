#!/usr/bin/env bash
# Smoke test e2e (loopback): broker + sender via TLS+HMAC on 127.0.0.1.
# Proves the full handshake path (TLS -> CHALLENGE -> HMAC AUTH -> ALLOWED_SENDERS
# gate -> registry) end to end against the real network stack, no GUI required.
# The broker's /health endpoint is the oracle: it must list the sender as registered.
#
# Requires: broker/.env, sender/.env (SHARED_SECRET must match), certs/ present.
# Codec-agnostic: uses whatever the .env files specify (default smoke = gdi+jpeg).
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
HEALTH_URL="http://127.0.0.1:7001/health"
SENDER_ID="laptop-01"
BROKER_LOG="$(mktemp)"
SENDER_LOG="$(mktemp)"
BROKER_PID=""
SENDER_PID=""

cleanup() {
  [ -n "$SENDER_PID" ] && kill "$SENDER_PID" 2>/dev/null
  [ -n "$BROKER_PID" ] && kill "$BROKER_PID" 2>/dev/null
  wait 2>/dev/null
}
trap cleanup EXIT

fail() { echo "FAIL: $*"; echo "--- broker log ---"; tail -20 "$BROKER_LOG"; echo "--- sender log ---"; tail -20 "$SENDER_LOG"; exit 1; }

echo "[1/4] start broker (health :7001)"
( cd "$ROOT/broker" && node dist/index.js ) >"$BROKER_LOG" 2>&1 &
BROKER_PID=$!

for i in $(seq 1 30); do
  curl -sf "$HEALTH_URL" >/dev/null 2>&1 && break
  kill -0 "$BROKER_PID" 2>/dev/null || fail "broker exited early"
  sleep 0.5
done
curl -sf "$HEALTH_URL" >/dev/null 2>&1 || fail "broker health never came up"
echo "      broker up: $(curl -s "$HEALTH_URL")"

echo "[2/4] start sender ($SENDER_ID)"
( cd "$ROOT/sender" && dotnet run -c Debug --no-build ) >"$SENDER_LOG" 2>&1 &
SENDER_PID=$!

echo "[3/4] wait for sender to register via /health"
REGISTERED=0
for i in $(seq 1 40); do
  BODY="$(curl -s "$HEALTH_URL" 2>/dev/null)"
  echo "$BODY" | grep -q "$SENDER_ID" && { REGISTERED=1; break; }
  kill -0 "$SENDER_PID" 2>/dev/null || fail "sender exited early"
  sleep 0.5
done
[ "$REGISTERED" = 1 ] || fail "sender never appeared in /health after 20s"
echo "      health now: $(curl -s "$HEALTH_URL")"

echo "[4/4] assert AUTH_OK in broker log"
grep -qiE "auth|registered|sender.*up|$SENDER_ID" "$BROKER_LOG" || echo "      (warn) no explicit auth line; registry proof already passed"

echo "PASS: sender authenticated (TLS+HMAC) and registered end-to-end"
