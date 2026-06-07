import * as crypto from 'node:crypto';

export const PROTOCOL_VERSION = 0x01;

export const MessageType = {
  HELLO:       0x01,
  AUTH:        0x02,
  AUTH_OK:     0x03,
  AUTH_FAIL:   0x04,
  CHALLENGE:   0x05,
  VIDEO_FRAME:  0x10,
  REQUEST_IDR:  0x11,
  INPUT_EVENT:  0x20,
  SENDER_UP:   0x30,
  SENDER_DOWN: 0x31,
  PING:        0x40,
  PONG:        0x41,
  BYE:         0x50,
  FILE_HEADER:     0x60,
  CLIPBOARD:       0x61,
  FILE_CHUNK:      0x62,
  FILE_DONE:       0x63,
  FILE_ERROR:      0x64,
  MONITOR_LIST:    0x70,
  SELECT_MONITOR:  0x71,
  AUDIO_FRAME:     0x80,
  REMOTE_REBOOT:   0x90,
} as const;

export type MessageTypeValue = (typeof MessageType)[keyof typeof MessageType];

export const HEADER_SIZE    = 22;
export const PEER_ID_OFFSET = 2;
export const PEER_ID_SIZE   = 16;
export const LENGTH_OFFSET  = 18;
export const NONCE_SIZE     = 32;

export const PING_INTERVAL_MS     = 5_000;
export const PONG_TIMEOUT_MS      = 15_000;
export const HANDSHAKE_TIMEOUT_MS = 10_000;
export const MAX_FRAME_BYTES      = 8 * 1024 * 1024; // 8 MB

export interface ParsedHeader {
  version: number;
  type: number;
  peerId: string;
  length: number;
}

export function decodePeerId(buf: Buffer, offset = PEER_ID_OFFSET): string {
  const slice = buf.subarray(offset, offset + PEER_ID_SIZE);
  const nullIdx = slice.indexOf(0);
  return slice.subarray(0, nullIdx === -1 ? PEER_ID_SIZE : nullIdx).toString('utf8');
}

export function parseHeader(buf: Buffer): ParsedHeader {
  return {
    version: buf[0],
    type:    buf[1],
    peerId:  decodePeerId(buf),
    length:  buf.readUInt32BE(LENGTH_OFFSET),
  };
}

export function buildEnvelope(type: number, peerId: string, payload: Buffer): Buffer {
  const header = Buffer.allocUnsafe(HEADER_SIZE);
  header[0] = PROTOCOL_VERSION;
  header[1] = type;

  const peerIdBuf = Buffer.alloc(PEER_ID_SIZE, 0);
  const peerIdBytes = Buffer.from(peerId.slice(0, PEER_ID_SIZE), 'utf8');
  peerIdBytes.copy(peerIdBuf);
  peerIdBuf.copy(header, PEER_ID_OFFSET);

  header.writeUInt32BE(payload.length, LENGTH_OFFSET);
  return Buffer.concat([header, payload]);
}

export const build = {
  challenge: (nonce: Buffer) =>
    buildEnvelope(MessageType.CHALLENGE, '', nonce),

  authOk: (peerId: string) =>
    buildEnvelope(MessageType.AUTH_OK, peerId, Buffer.alloc(0)),

  authFail: (peerId: string, reason: string) =>
    buildEnvelope(MessageType.AUTH_FAIL, peerId, Buffer.from(JSON.stringify({ reason }))),

  senderUp: (agentId: string, mac?: string, version?: string) =>
    buildEnvelope(MessageType.SENDER_UP, 'receiver', Buffer.from(JSON.stringify({ agentId, mac, version }))),

  senderDown: (agentId: string) =>
    buildEnvelope(MessageType.SENDER_DOWN, 'receiver', Buffer.from(JSON.stringify({ agentId }))),

  pong: (pingPayload: Buffer) =>
    buildEnvelope(MessageType.PONG, '', pingPayload),

  ping: (tsMs: bigint) => {
    const payload = Buffer.allocUnsafe(8);
    payload.writeBigUInt64BE(tsMs);
    return buildEnvelope(MessageType.PING, '', payload);
  },
};

export function generateNonce(): Buffer {
  return crypto.randomBytes(NONCE_SIZE);
}

export function verifyHmac(nonce: Buffer, hmacBytes: Buffer, secret: string): boolean {
  const expected = crypto.createHmac('sha256', secret).update(nonce).digest();
  if (expected.length !== hmacBytes.length) return false;
  return crypto.timingSafeEqual(expected, hmacBytes);
}
