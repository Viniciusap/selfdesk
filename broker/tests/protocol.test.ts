import {
  HEADER_SIZE,
  PEER_ID_SIZE,
  MessageType,
  PROTOCOL_VERSION,
  buildEnvelope,
  parseHeader,
  decodePeerId,
  generateNonce,
  verifyHmac,
  build,
  NONCE_SIZE,
} from '../src/protocol';

describe('buildEnvelope / parseHeader', () => {
  it('produces correct-sized envelope', () => {
    const payload = Buffer.from('hello');
    const msg     = buildEnvelope(MessageType.HELLO, 'laptop-01', payload);
    expect(msg.length).toBe(HEADER_SIZE + payload.length);
  });

  it('VERSION byte is 0x01', () => {
    const msg = buildEnvelope(MessageType.AUTH_OK, '', Buffer.alloc(0));
    expect(msg[0]).toBe(PROTOCOL_VERSION);
  });

  it('TYPE byte is correct', () => {
    const msg = buildEnvelope(MessageType.AUTH_OK, 'r', Buffer.alloc(0));
    expect(msg[1]).toBe(MessageType.AUTH_OK);
  });

  it('PEER_ID is padded with \\0 to 16 bytes', () => {
    const peerId = 'laptop-01';
    const msg    = buildEnvelope(MessageType.HELLO, peerId, Buffer.alloc(0));
    const decoded = decodePeerId(msg);
    expect(decoded).toBe(peerId);
    // bytes after the ID must be \0
    const peerIdSlice = msg.subarray(2, 18);
    const expectedPadding = PEER_ID_SIZE - Buffer.from(peerId).length;
    const tail = peerIdSlice.subarray(Buffer.from(peerId).length);
    expect(tail.every((b) => b === 0)).toBe(true);
    expect(expectedPadding).toBeGreaterThan(0);
  });

  it('LENGTH field is correct big-endian uint32', () => {
    const payload = Buffer.alloc(300, 0xab);
    const msg     = buildEnvelope(MessageType.VIDEO_FRAME, '', payload);
    const length  = msg.readUInt32BE(18);
    expect(length).toBe(300);
  });

  it('PEER_ID is truncated when > 16 bytes', () => {
    const longId = 'a'.repeat(20);
    const msg    = buildEnvelope(MessageType.HELLO, longId, Buffer.alloc(0));
    const decoded = decodePeerId(msg);
    expect(decoded.length).toBeLessThanOrEqual(PEER_ID_SIZE);
  });

  it('parseHeader returns all fields correctly', () => {
    const payload = Buffer.from('payload');
    const msg     = buildEnvelope(MessageType.AUTH, 'agent-x', payload);
    const hdr     = parseHeader(msg);
    expect(hdr.version).toBe(PROTOCOL_VERSION);
    expect(hdr.type).toBe(MessageType.AUTH);
    expect(hdr.peerId).toBe('agent-x');
    expect(hdr.length).toBe(payload.length);
  });

  it('empty PEER_ID decodes to empty string', () => {
    const msg = buildEnvelope(MessageType.PONG, '', Buffer.alloc(0));
    expect(decodePeerId(msg)).toBe('');
  });
});

describe('HMAC', () => {
  it('verifyHmac accepts a valid HMAC', () => {
    const nonce  = generateNonce();
    const secret = 'super-secret-key';
    const crypto = require('node:crypto') as typeof import('node:crypto');
    const hmac   = crypto.createHmac('sha256', secret).update(nonce).digest();
    expect(verifyHmac(nonce, hmac, secret)).toBe(true);
  });

  it('verifyHmac rejects HMAC with wrong secret', () => {
    const nonce = generateNonce();
    const crypto = require('node:crypto') as typeof import('node:crypto');
    const hmac  = crypto.createHmac('sha256', 'wrong').update(nonce).digest();
    expect(verifyHmac(nonce, hmac, 'correct')).toBe(false);
  });

  it('verifyHmac rejects wrong-size payload', () => {
    const nonce = generateNonce();
    expect(verifyHmac(nonce, Buffer.alloc(16), 'any')).toBe(false);
  });
});

describe('nonce', () => {
  it('generateNonce returns 32 random bytes', () => {
    const a = generateNonce();
    const b = generateNonce();
    expect(a.length).toBe(NONCE_SIZE);
    expect(a.equals(b)).toBe(false);
  });
});

describe('build helpers', () => {
  it('build.challenge produces 32-byte payload', () => {
    const nonce = generateNonce();
    const msg   = build.challenge(nonce);
    const hdr   = parseHeader(msg);
    expect(hdr.type).toBe(MessageType.CHALLENGE);
    expect(hdr.length).toBe(32);
  });

  it('build.authOk has empty payload', () => {
    const msg = build.authOk('laptop-01');
    const hdr = parseHeader(msg);
    expect(hdr.type).toBe(MessageType.AUTH_OK);
    expect(hdr.length).toBe(0);
  });

  it('build.authFail has JSON with reason field', () => {
    const msg  = build.authFail('x', 'test reason');
    const hdr  = parseHeader(msg);
    const body = JSON.parse(msg.subarray(HEADER_SIZE).toString());
    expect(hdr.type).toBe(MessageType.AUTH_FAIL);
    expect(body.reason).toBe('test reason');
  });

  it('build.senderUp has JSON with agentId field', () => {
    const msg  = build.senderUp('laptop-01');
    const hdr  = parseHeader(msg);
    const body = JSON.parse(msg.subarray(HEADER_SIZE).toString());
    expect(hdr.type).toBe(MessageType.SENDER_UP);
    expect(hdr.peerId).toBe('receiver');
    expect(body.agentId).toBe('laptop-01');
  });

  it('build.ping payload is bigint big-endian 8 bytes', () => {
    const ts  = BigInt(Date.now());
    const msg = build.ping(ts);
    const hdr = parseHeader(msg);
    expect(hdr.type).toBe(MessageType.PING);
    const echoTs = msg.subarray(HEADER_SIZE).readBigUInt64BE(0);
    expect(echoTs).toBe(ts);
  });
});
