/**
 * Testes de integração: broker real com TLS, cliente mock fazendo handshake completo.
 * Usa `selfsigned` para gerar certificados de teste em memória.
 */

import * as tls from 'node:tls';
import * as net from 'node:net';
import * as crypto from 'node:crypto';
import pino from 'pino';
import selfsigned from 'selfsigned';
import { Registry } from '../src/registry';
import { Router } from '../src/router';
import { createServer } from '../src/server';
import {
  HEADER_SIZE,
  MessageType,
  buildEnvelope,
  parseHeader,
} from '../src/protocol';

const SECRET = 'integration-test-secret-32chars!!';
const ALLOWED = new Set(['laptop-01']);

let serverCert: Buffer;
let serverKey:  Buffer;
let port:       number;
let server:     tls.Server;

beforeAll(async () => {
  const attrs = [{ name: 'commonName', value: 'selfdesk-test' }];
  const pems  = selfsigned.generate(attrs, {
    days: 1,
    keySize: 2048,
    algorithm: 'sha256',
    extensions: [{ name: 'subjectAltName', altNames: [{ type: 7, ip: '127.0.0.1' }] }],
  });
  serverCert = Buffer.from(pems.cert);
  serverKey  = Buffer.from(pems.private);

  const log      = pino({ level: 'silent' });
  const cfg      = { listenPort: 0, sharedSecret: SECRET, allowedSenders: ALLOWED, tlsCertPath: '', tlsKeyPath: '', logLevel: 'silent' };
  const registry = new Registry(log);
  const router   = new Router(registry, log);
  server = createServer(cfg, { cert: serverCert, key: serverKey }, router, log);

  await new Promise<void>((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      port = (server.address() as net.AddressInfo).port;
      resolve();
    });
  });
});

afterAll(() => {
  server.close();
});

function tlsConnect(): Promise<tls.TLSSocket> {
  return new Promise((resolve, reject) => {
    const sock = tls.connect({ host: '127.0.0.1', port, ca: serverCert, rejectUnauthorized: true }, () => resolve(sock));
    sock.once('error', reject);
  });
}

function readFrame(sock: tls.TLSSocket): Promise<{ type: number; peerId: string; payload: Buffer }> {
  return new Promise((resolve, reject) => {
    let buf = Buffer.alloc(0);
    const onData = (chunk: Buffer) => {
      buf = Buffer.concat([buf, chunk]);
      if (buf.length >= HEADER_SIZE) {
        const hdr   = parseHeader(buf);
        const total = HEADER_SIZE + hdr.length;
        if (buf.length >= total) {
          sock.off('data', onData);
          resolve({ type: hdr.type, peerId: hdr.peerId, payload: buf.subarray(HEADER_SIZE, total) });
        }
      }
    };
    sock.on('data', onData);
    sock.once('error', reject);
  });
}

async function doHandshake(sock: tls.TLSSocket, role: 'sender' | 'receiver', agentId: string, secret: string) {
  sock.write(buildEnvelope(MessageType.HELLO, agentId, Buffer.from(JSON.stringify({ version: 1, role, agentId }))));
  const challenge = await readFrame(sock);
  expect(challenge.type).toBe(MessageType.CHALLENGE);
  expect(challenge.payload.length).toBe(32);

  const hmac = crypto.createHmac('sha256', secret).update(challenge.payload).digest();
  sock.write(buildEnvelope(MessageType.AUTH, agentId, hmac));
  const result = await readFrame(sock);
  return result;
}

describe('Handshake completo', () => {
  it('sender autentica com segredo correto → AUTH_OK', async () => {
    const sock   = await tlsConnect();
    const result = await doHandshake(sock, 'sender', 'laptop-01', SECRET);
    expect(result.type).toBe(MessageType.AUTH_OK);
    sock.destroy();
  });

  it('receiver autentica com segredo correto → AUTH_OK', async () => {
    const sock   = await tlsConnect();
    const result = await doHandshake(sock, 'receiver', 'receiver', SECRET);
    expect(result.type).toBe(MessageType.AUTH_OK);
    sock.destroy();
  });

  it('sender com segredo errado → AUTH_FAIL', async () => {
    const sock   = await tlsConnect();
    const result = await doHandshake(sock, 'sender', 'laptop-01', 'wrong-secret');
    expect(result.type).toBe(MessageType.AUTH_FAIL);
    const body = JSON.parse(result.payload.toString());
    expect(body.reason).toBeDefined();
    sock.destroy();
  });

  it('agentId não permitido → AUTH_FAIL imediato (sem challenge)', async () => {
    const sock = await tlsConnect();
    sock.write(buildEnvelope(MessageType.HELLO, 'unknown-agent', Buffer.from(JSON.stringify({ version: 1, role: 'sender', agentId: 'unknown-agent' }))));
    const result = await readFrame(sock);
    expect(result.type).toBe(MessageType.AUTH_FAIL);
    sock.destroy();
  });
});

describe('SENDER_UP ao conectar', () => {
  it('receiver recebe SENDER_UP quando sender conecta', async () => {
    const rxSock = await tlsConnect();
    await doHandshake(rxSock, 'receiver', 'receiver', SECRET);

    const senderPromise = readFrame(rxSock);

    const txSock = await tlsConnect();
    await doHandshake(txSock, 'sender', 'laptop-01', SECRET);

    const senderUp = await senderPromise;
    expect(senderUp.type).toBe(MessageType.SENDER_UP);
    const body = JSON.parse(senderUp.payload.toString());
    expect(body.agentId).toBe('laptop-01');

    txSock.destroy();
    rxSock.destroy();
  });
});

describe('PING/PONG', () => {
  it('broker responde PONG para PING do sender', async () => {
    const sock = await tlsConnect();
    await doHandshake(sock, 'sender', 'laptop-01', SECRET);

    const tsMs  = BigInt(Date.now());
    const pingPayload = Buffer.allocUnsafe(8);
    pingPayload.writeBigUInt64BE(tsMs);
    sock.write(buildEnvelope(MessageType.PING, '', pingPayload));

    const pong = await readFrame(sock);
    expect(pong.type).toBe(MessageType.PONG);
    const echoTs = pong.payload.readBigUInt64BE(0);
    expect(echoTs).toBe(tsMs);
    sock.destroy();
  });
});
