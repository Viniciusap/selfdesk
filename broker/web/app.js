// SelfDesk Web Viewer — issue #12, milestone 1 (read-only: video display, no input).
//
// Reimplements the wire protocol (shared/protocol/protocol.ts / broker/src/protocol.ts)
// in plain browser JS — no bundler, no dependencies. Every WebSocket message the
// broker's `ws` library sends corresponds to exactly one `Connection.send()` call on
// the broker side, i.e. exactly one complete envelope — WebSocket's own message
// framing means the browser never needs to reassemble a byte stream the way the
// broker's drainFrames() does for the raw TCP path.
'use strict';

const HEADER_SIZE = 22;
const PEER_ID_OFFSET = 2;
const PEER_ID_SIZE = 16;
const LENGTH_OFFSET = 18;

const MessageType = {
  HELLO: 0x01,
  AUTH: 0x02,
  AUTH_OK: 0x03,
  AUTH_FAIL: 0x04,
  CHALLENGE: 0x05,
  VIDEO_FRAME: 0x10,
  SENDER_UP: 0x30,
  SENDER_DOWN: 0x31,
  PING: 0x40,
  PONG: 0x41,
  BYE: 0x50,
};

const VideoFrameOffsets = {
  Timestamp: 0,
  Width: 8,
  Height: 10,
  Codec: 12,
  Flags: 13,
  Data: 14,
};
const CODEC_JPEG = 0x01;

function buildEnvelope(type, peerId, payload) {
  const peerIdBytes = new TextEncoder().encode(peerId).slice(0, PEER_ID_SIZE);
  const buf = new Uint8Array(HEADER_SIZE + payload.length);
  buf[0] = 0x01; // PROTOCOL_VERSION
  buf[1] = type;
  buf.set(peerIdBytes, PEER_ID_OFFSET);
  new DataView(buf.buffer).setUint32(LENGTH_OFFSET, payload.length, false);
  buf.set(payload, HEADER_SIZE);
  return buf;
}

function parseHeader(buf) {
  const view = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
  const type = buf[1];
  const peerIdBytes = buf.subarray(PEER_ID_OFFSET, PEER_ID_OFFSET + PEER_ID_SIZE);
  let nullIdx = peerIdBytes.indexOf(0);
  if (nullIdx === -1) nullIdx = PEER_ID_SIZE;
  const peerId = new TextDecoder().decode(peerIdBytes.subarray(0, nullIdx));
  const length = view.getUint32(LENGTH_OFFSET, false);
  return { type, peerId, length };
}

async function hmacSha256(secret, data) {
  const key = await crypto.subtle.importKey(
    'raw', new TextEncoder().encode(secret),
    { name: 'HMAC', hash: 'SHA-256' }, false, ['sign'],
  );
  const sig = await crypto.subtle.sign('HMAC', key, data);
  return new Uint8Array(sig);
}

// ── UI wiring ──────────────────────────────────────────────────────────────

const hostInput = document.getElementById('host');
const portInput = document.getElementById('wsPort');
const secretInput = document.getElementById('secret');
const connectBtn = document.getElementById('connectBtn');
const statusEl = document.getElementById('status');
const sendersEl = document.getElementById('senders');
const canvas = document.getElementById('video');
const placeholder = document.getElementById('placeholder');
const ctx = canvas.getContext('2d');

let ws = null;
let selectedSender = null;
const senders = new Map(); // agentId -> { mac, version }

function setStatus(text, cls) {
  statusEl.textContent = text;
  statusEl.className = cls || '';
}

function renderSenderList() {
  sendersEl.innerHTML = '';
  for (const [agentId] of senders) {
    const li = document.createElement('li');
    li.className = agentId === selectedSender ? 'selected' : '';
    const dot = document.createElement('span');
    dot.className = 'dot';
    li.appendChild(dot);
    li.appendChild(document.createTextNode(agentId));
    li.addEventListener('click', () => {
      selectedSender = agentId;
      canvas.style.display = 'none';
      placeholder.style.display = '';
      placeholder.textContent = `Waiting for video from ${agentId}...`;
      renderSenderList();
    });
    sendersEl.appendChild(li);
  }
}

connectBtn.addEventListener('click', () => {
  if (ws) { ws.close(); return; }
  connect();
});

function connect() {
  const host = hostInput.value.trim();
  const port = portInput.value.trim();
  const secret = secretInput.value;
  if (!host || !port || !secret) {
    setStatus('Host, port, and secret are required', 'err');
    return;
  }

  const agentId = 'web-' + Math.random().toString(36).slice(2, 10);
  setStatus('Connecting...', '');
  connectBtn.disabled = true;

  ws = new WebSocket(`wss://${host}:${port}/ws`);
  ws.binaryType = 'arraybuffer';

  ws.addEventListener('open', () => {
    const hello = new TextEncoder().encode(JSON.stringify({ version: 1, role: 'receiver', agentId }));
    ws.send(buildEnvelope(MessageType.HELLO, agentId, hello));
  });

  ws.addEventListener('message', async (event) => {
    const buf = new Uint8Array(event.data);
    if (buf.length < HEADER_SIZE) return;
    const header = parseHeader(buf);
    const payload = buf.subarray(HEADER_SIZE, HEADER_SIZE + header.length);

    switch (header.type) {
      case MessageType.CHALLENGE: {
        const hmac = await hmacSha256(secret, payload);
        ws.send(buildEnvelope(MessageType.AUTH, agentId, hmac));
        break;
      }
      case MessageType.AUTH_OK:
        setStatus('Connected', 'ok');
        connectBtn.textContent = 'Disconnect';
        connectBtn.disabled = false;
        break;
      case MessageType.AUTH_FAIL:
        setStatus('Authentication failed — check the shared secret', 'err');
        ws.close();
        break;
      case MessageType.SENDER_UP: {
        const body = JSON.parse(new TextDecoder().decode(payload));
        senders.set(body.agentId, { mac: body.mac, version: body.version });
        renderSenderList();
        break;
      }
      case MessageType.SENDER_DOWN: {
        const body = JSON.parse(new TextDecoder().decode(payload));
        senders.delete(body.agentId);
        if (selectedSender === body.agentId) {
          selectedSender = null;
          canvas.style.display = 'none';
          placeholder.style.display = '';
          placeholder.textContent = `${body.agentId} disconnected`;
        }
        renderSenderList();
        break;
      }
      case MessageType.VIDEO_FRAME:
        await onVideoFrame(header.peerId, payload);
        break;
      case MessageType.PING:
        ws.send(buildEnvelope(MessageType.PONG, '', payload));
        break;
      default:
        break; // milestone 1 doesn't send/expect anything else
    }
  });

  ws.addEventListener('close', () => {
    setStatus('Disconnected', 'err');
    connectBtn.textContent = 'Connect';
    connectBtn.disabled = false;
    ws = null;
    senders.clear();
    renderSenderList();
    canvas.style.display = 'none';
    placeholder.style.display = '';
    placeholder.textContent = 'Not connected';
  });

  ws.addEventListener('error', () => {
    setStatus('Connection error', 'err');
  });
}

async function onVideoFrame(senderId, payload) {
  if (senderId !== selectedSender) return;
  if (payload.length < VideoFrameOffsets.Data) return;

  const view = new DataView(payload.buffer, payload.byteOffset, payload.byteLength);
  const codec = payload[VideoFrameOffsets.Codec];
  if (codec !== CODEC_JPEG) {
    placeholder.style.display = '';
    placeholder.textContent = 'This sender uses H264 — the web viewer only supports JPEG so far';
    canvas.style.display = 'none';
    return;
  }

  const width = view.getUint16(VideoFrameOffsets.Width, false);
  const height = view.getUint16(VideoFrameOffsets.Height, false);
  const jpegBytes = payload.subarray(VideoFrameOffsets.Data);

  let bitmap;
  try {
    bitmap = await createImageBitmap(new Blob([jpegBytes], { type: 'image/jpeg' }));
  } catch {
    return; // a corrupt/partial frame — drop it, next frame will recover
  }

  if (canvas.width !== width || canvas.height !== height) {
    canvas.width = width;
    canvas.height = height;
  }
  ctx.drawImage(bitmap, 0, 0);
  bitmap.close();

  if (canvas.style.display === 'none') {
    canvas.style.display = '';
    placeholder.style.display = 'none';
  }
}
