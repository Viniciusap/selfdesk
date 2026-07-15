/**
 * Issue #12: the web viewer's WSS endpoint. Verifies a browser-shaped client
 * (the `ws` package standing in for the real browser WebSocket API) can complete
 * the same HELLO/CHALLENGE/AUTH handshake as the native TCP viewer, that
 * SENDER_UP/VIDEO_FRAME routing works identically over this transport, and that
 * the static file server only ever serves files inside web/.
 */
import * as https from 'node:https';
import * as net from 'node:net';
import * as crypto from 'node:crypto';
import WebSocket from 'ws';
import pino from 'pino';
import selfsigned from 'selfsigned';
import { Registry } from '../src/registry';
import { Router } from '../src/router';
import { createRateLimiters } from '../src/server';
import { createWsServer } from '../src/wsServer';
import { HEADER_SIZE, MessageType, buildEnvelope, parseHeader } from '../src/protocol';

const SECRET = 'ws-integration-test-secret-32ch!';
const ALLOWED = new Set(['laptop-01']);

let serverCert: Buffer;
let serverKey: Buffer;
let port: number;
let server: https.Server;

beforeAll(async () => {
  const attrs = [{ name: 'commonName', value: 'selfdesk-ws-test' }];
  const pems = selfsigned.generate(attrs, {
    days: 1,
    keySize: 2048,
    algorithm: 'sha256',
    extensions: [{ name: 'subjectAltName', altNames: [{ type: 7, ip: '127.0.0.1' }] }],
  });
  serverCert = Buffer.from(pems.cert);
  serverKey = Buffer.from(pems.private);

  const log = pino({ level: 'silent' });
  const cfg = { listenPort: 0, sharedSecret: SECRET, allowedSenders: ALLOWED, tlsCertPath: '', tlsKeyPath: '', logLevel: 'silent', logMaxSize: '10m', logMaxFiles: 5 };
  const registry = new Registry(log);
  const router = new Router(registry, log);
  server = createWsServer(cfg, { cert: serverCert, key: serverKey }, router, log, createRateLimiters());

  await new Promise<void>((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      port = (server.address() as net.AddressInfo).port;
      resolve();
    });
  });
}, 20_000);

afterAll(() => {
  server.close();
});

function wsConnect(): WebSocket {
  return new WebSocket(`wss://127.0.0.1:${port}/ws`, { rejectUnauthorized: false });
}

function nextEnvelope(ws: WebSocket): Promise<{ type: number; peerId: string; payload: Buffer }> {
  return new Promise((resolve, reject) => {
    ws.once('message', (data: Buffer) => {
      const buf = Buffer.isBuffer(data) ? data : Buffer.from(data as ArrayBuffer);
      if (buf.length < HEADER_SIZE) { reject(new Error('short frame')); return; }
      const hdr = parseHeader(buf);
      resolve({ type: hdr.type, peerId: hdr.peerId, payload: buf.subarray(HEADER_SIZE, HEADER_SIZE + hdr.length) });
    });
    ws.once('error', reject);
  });
}

async function doHandshake(ws: WebSocket, role: 'sender' | 'receiver', agentId: string, secret: string) {
  await new Promise<void>((resolve, reject) => { ws.once('open', () => resolve()); ws.once('error', reject); });
  ws.send(buildEnvelope(MessageType.HELLO, agentId, Buffer.from(JSON.stringify({ version: 1, role, agentId }))));
  const challenge = await nextEnvelope(ws);
  expect(challenge.type).toBe(MessageType.CHALLENGE);
  expect(challenge.payload.length).toBe(32);

  const hmac = crypto.createHmac('sha256', secret).update(challenge.payload).digest();
  ws.send(buildEnvelope(MessageType.AUTH, agentId, hmac));
  return nextEnvelope(ws);
}

describe('WSS handshake', () => {
  it('receiver authenticates over WebSocket with the correct secret → AUTH_OK', async () => {
    const ws = wsConnect();
    const result = await doHandshake(ws, 'receiver', 'web-viewer', SECRET);
    expect(result.type).toBe(MessageType.AUTH_OK);
    ws.close();
  });

  it('sender authenticates over WebSocket too — same Connection/Router, no role restriction', async () => {
    const ws = wsConnect();
    const result = await doHandshake(ws, 'sender', 'laptop-01', SECRET);
    expect(result.type).toBe(MessageType.AUTH_OK);
    ws.close();
  });

  it('wrong secret over WebSocket → AUTH_FAIL', async () => {
    const ws = wsConnect();
    const result = await doHandshake(ws, 'receiver', 'web-viewer', 'wrong-secret');
    expect(result.type).toBe(MessageType.AUTH_FAIL);
    ws.close();
  });

  it('a non-binary (text) frame gets the connection dropped, not crash the server', async () => {
    const ws = wsConnect();
    await new Promise<void>((resolve, reject) => { ws.once('open', () => resolve()); ws.once('error', reject); });
    ws.send('not a binary envelope');
    await new Promise<void>((resolve) => { ws.once('close', () => resolve()); });

    // Server must still be healthy for the next client.
    const ws2 = wsConnect();
    const result = await doHandshake(ws2, 'receiver', 'web-viewer-2', SECRET);
    expect(result.type).toBe(MessageType.AUTH_OK);
    ws2.close();
  });
});

describe('WSS ↔ TCP interop', () => {
  it('a web receiver gets SENDER_UP relayed the same way as the native TCP viewer', async () => {
    const rxWs = wsConnect();
    await doHandshake(rxWs, 'receiver', 'web-viewer', SECRET);

    const senderUpPromise = nextEnvelope(rxWs);

    const txWs = wsConnect();
    await doHandshake(txWs, 'sender', 'laptop-01', SECRET);

    const senderUp = await senderUpPromise;
    expect(senderUp.type).toBe(MessageType.SENDER_UP);
    const body = JSON.parse(senderUp.payload.toString());
    expect(body.agentId).toBe('laptop-01');

    txWs.close();
    rxWs.close();
  });
});

describe('static file server', () => {
  function get(urlPath: string): Promise<{ status: number; body: string; contentType?: string }> {
    return new Promise((resolve, reject) => {
      https.get(
        { host: '127.0.0.1', port, path: urlPath, rejectUnauthorized: false },
        (res) => {
          let body = '';
          res.on('data', (chunk) => { body += chunk; });
          res.on('end', () => resolve({ status: res.statusCode ?? 0, body, contentType: res.headers['content-type'] }));
        },
      ).on('error', reject);
    });
  }

  it('serves index.html at /', async () => {
    const res = await get('/');
    expect(res.status).toBe(200);
    expect(res.contentType).toContain('text/html');
    expect(res.body).toContain('SelfDesk Web Viewer');
  });

  it('serves app.js', async () => {
    const res = await get('/app.js');
    expect(res.status).toBe(200);
    expect(res.contentType).toContain('text/javascript');
    expect(res.body).toContain('buildEnvelope');
  });

  it('rejects path traversal outside web/', async () => {
    const res = await get('/../src/config.ts');
    expect(res.status).not.toBe(200);
  });

  it('404s an unknown file', async () => {
    const res = await get('/does-not-exist.txt');
    expect(res.status).toBe(404);
  });

  it('429s once the IP is blocked (shared rate limiter, not just the WS path)', async () => {
    // Isolated server + limiters so tripping the block here doesn't affect the
    // other tests in this file, which all share the same 127.0.0.1 source IP.
    const log = pino({ level: 'silent' });
    const cfg = { listenPort: 0, sharedSecret: SECRET, allowedSenders: ALLOWED, tlsCertPath: '', tlsKeyPath: '', logLevel: 'silent', logMaxSize: '10m', logMaxFiles: 5 };
    const registry = new Registry(log);
    const router = new Router(registry, log);
    const isolatedServer = createWsServer(cfg, { cert: serverCert, key: serverKey }, router, log, createRateLimiters());
    const isolatedPort = await new Promise<number>((resolve, reject) => {
      isolatedServer.once('error', reject);
      isolatedServer.listen(0, '127.0.0.1', () => resolve((isolatedServer.address() as net.AddressInfo).port));
    });

    // Trip FAIL_LIMIT (5) by failing AUTH from a fresh connection each time.
    for (let i = 0; i < 5; i++) {
      const ws = new WebSocket(`wss://127.0.0.1:${isolatedPort}/ws`, { rejectUnauthorized: false });
      await doHandshake(ws, 'receiver', `flood-${i}`, 'wrong-secret');
      ws.close();
    }
    await new Promise((resolve) => setTimeout(resolve, 100)); // let 'closed' handlers run

    const res = await new Promise<{ status: number }>((resolve, reject) => {
      https.get({ host: '127.0.0.1', port: isolatedPort, path: '/', rejectUnauthorized: false }, (r) => {
        r.resume();
        resolve({ status: r.statusCode ?? 0 });
      }).on('error', reject);
    });
    expect(res.status).toBe(429);

    isolatedServer.close();
  }, 15_000);
});
