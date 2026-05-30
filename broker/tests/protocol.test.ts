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
  it('produz envelope de tamanho correto', () => {
    const payload = Buffer.from('hello');
    const msg     = buildEnvelope(MessageType.HELLO, 'laptop-01', payload);
    expect(msg.length).toBe(HEADER_SIZE + payload.length);
  });

  it('VERSION byte é 0x01', () => {
    const msg = buildEnvelope(MessageType.AUTH_OK, '', Buffer.alloc(0));
    expect(msg[0]).toBe(PROTOCOL_VERSION);
  });

  it('TYPE byte é correto', () => {
    const msg = buildEnvelope(MessageType.AUTH_OK, 'r', Buffer.alloc(0));
    expect(msg[1]).toBe(MessageType.AUTH_OK);
  });

  it('PEER_ID é padded com \\0 a 16 bytes (big-endian)', () => {
    const peerId = 'laptop-01';
    const msg    = buildEnvelope(MessageType.HELLO, peerId, Buffer.alloc(0));
    const decoded = decodePeerId(msg);
    expect(decoded).toBe(peerId);
    // bytes após o ID devem ser \0
    const peerIdSlice = msg.subarray(2, 18);
    const expectedPadding = PEER_ID_SIZE - Buffer.from(peerId).length;
    const tail = peerIdSlice.subarray(Buffer.from(peerId).length);
    expect(tail.every((b) => b === 0)).toBe(true);
    expect(expectedPadding).toBeGreaterThan(0);
  });

  it('LENGTH big-endian uint32 correto', () => {
    const payload = Buffer.alloc(300, 0xab);
    const msg     = buildEnvelope(MessageType.VIDEO_FRAME, '', payload);
    const length  = msg.readUInt32BE(18);
    expect(length).toBe(300);
  });

  it('PEER_ID truncado se > 16 bytes', () => {
    const longId = 'a'.repeat(20);
    const msg    = buildEnvelope(MessageType.HELLO, longId, Buffer.alloc(0));
    const decoded = decodePeerId(msg);
    expect(decoded.length).toBeLessThanOrEqual(PEER_ID_SIZE);
  });

  it('parseHeader retorna todos os campos corretamente', () => {
    const payload = Buffer.from('payload');
    const msg     = buildEnvelope(MessageType.AUTH, 'agent-x', payload);
    const hdr     = parseHeader(msg);
    expect(hdr.version).toBe(PROTOCOL_VERSION);
    expect(hdr.type).toBe(MessageType.AUTH);
    expect(hdr.peerId).toBe('agent-x');
    expect(hdr.length).toBe(payload.length);
  });

  it('PEER_ID vazio decodifica como string vazia', () => {
    const msg = buildEnvelope(MessageType.PONG, '', Buffer.alloc(0));
    expect(decodePeerId(msg)).toBe('');
  });
});

describe('HMAC', () => {
  it('verifyHmac aceita HMAC correto', () => {
    const nonce  = generateNonce();
    const secret = 'super-secret-key';
    const crypto = require('node:crypto') as typeof import('node:crypto');
    const hmac   = crypto.createHmac('sha256', secret).update(nonce).digest();
    expect(verifyHmac(nonce, hmac, secret)).toBe(true);
  });

  it('verifyHmac rejeita HMAC com segredo errado', () => {
    const nonce = generateNonce();
    const crypto = require('node:crypto') as typeof import('node:crypto');
    const hmac  = crypto.createHmac('sha256', 'wrong').update(nonce).digest();
    expect(verifyHmac(nonce, hmac, 'correct')).toBe(false);
  });

  it('verifyHmac rejeita payload de tamanho errado', () => {
    const nonce = generateNonce();
    expect(verifyHmac(nonce, Buffer.alloc(16), 'any')).toBe(false);
  });
});

describe('nonce', () => {
  it('generateNonce retorna 32 bytes aleatórios', () => {
    const a = generateNonce();
    const b = generateNonce();
    expect(a.length).toBe(NONCE_SIZE);
    expect(a.equals(b)).toBe(false);
  });
});

describe('build helpers', () => {
  it('build.challenge produz payload de 32 bytes', () => {
    const nonce = generateNonce();
    const msg   = build.challenge(nonce);
    const hdr   = parseHeader(msg);
    expect(hdr.type).toBe(MessageType.CHALLENGE);
    expect(hdr.length).toBe(32);
  });

  it('build.authOk tem payload vazio', () => {
    const msg = build.authOk('laptop-01');
    const hdr = parseHeader(msg);
    expect(hdr.type).toBe(MessageType.AUTH_OK);
    expect(hdr.length).toBe(0);
  });

  it('build.authFail tem JSON com reason', () => {
    const msg  = build.authFail('x', 'test reason');
    const hdr  = parseHeader(msg);
    const body = JSON.parse(msg.subarray(HEADER_SIZE).toString());
    expect(hdr.type).toBe(MessageType.AUTH_FAIL);
    expect(body.reason).toBe('test reason');
  });

  it('build.senderUp tem JSON com agentId', () => {
    const msg  = build.senderUp('laptop-01');
    const hdr  = parseHeader(msg);
    const body = JSON.parse(msg.subarray(HEADER_SIZE).toString());
    expect(hdr.type).toBe(MessageType.SENDER_UP);
    expect(hdr.peerId).toBe('receiver');
    expect(body.agentId).toBe('laptop-01');
  });

  it('build.ping payload é bigint big-endian 8 bytes', () => {
    const ts  = BigInt(Date.now());
    const msg = build.ping(ts);
    const hdr = parseHeader(msg);
    expect(hdr.type).toBe(MessageType.PING);
    const echoTs = msg.subarray(HEADER_SIZE).readBigUInt64BE(0);
    expect(echoTs).toBe(ts);
  });
});
