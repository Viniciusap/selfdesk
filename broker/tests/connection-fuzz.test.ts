/**
 * Issue #25: fuzz the live connection/framing layer (connection.ts's drainFrames +
 * handleHandshake), not just the pure parser functions. Throws random garbage at a
 * real TLS server both pre-auth and post-auth and asserts the broker never crashes
 * and stays responsive for legitimate connections afterward.
 */
import * as tls from 'node:tls';
import * as net from 'node:net';
import * as crypto from 'node:crypto';
import fc from 'fast-check';
import pino from 'pino';
import selfsigned from 'selfsigned';
import { Registry } from '../src/registry';
import { Router } from '../src/router';
import { RateLimiters, createRateLimiters, createServer } from '../src/server';
import { HEADER_SIZE, MessageType, buildEnvelope, parseHeader } from '../src/protocol';

const SECRET = 'fuzz-test-secret-32-characters!!';
const ALLOWED = new Set(['laptop-01']);

let serverCert: Buffer;
let serverKey: Buffer;
let port: number;
let server: tls.Server;
let limiters: RateLimiters;

beforeAll(async () => {
  const attrs = [{ name: 'commonName', value: 'selfdesk-fuzz' }];
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
  limiters = createRateLimiters();
  server = createServer(cfg, { cert: serverCert, key: serverKey }, router, log, limiters);

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

function tlsConnect(): Promise<tls.TLSSocket> {
  return new Promise((resolve, reject) => {
    const sock = tls.connect({ host: '127.0.0.1', port, ca: serverCert, rejectUnauthorized: true }, () => {
      // The broker may close the socket right after writing a response (e.g.
      // AUTH_FAIL, or a post-limit drop) — a late write/read on our end must not
      // surface as an unhandled 'error' event and crash the test process.
      sock.on('error', () => {});
      resolve(sock);
    });
    sock.once('error', reject);
  });
}

function readFrame(sock: tls.TLSSocket, timeoutMs = 500): Promise<{ type: number; peerId: string; payload: Buffer } | null> {
  return new Promise((resolve) => {
    let buf = Buffer.alloc(0);
    const timer = setTimeout(() => {
      sock.off('data', onData);
      resolve(null);
    }, timeoutMs);
    const onData = (chunk: Buffer) => {
      buf = Buffer.concat([buf, chunk]);
      if (buf.length >= HEADER_SIZE) {
        const hdr = parseHeader(buf);
        const total = HEADER_SIZE + hdr.length;
        if (buf.length >= total) {
          clearTimeout(timer);
          sock.off('data', onData);
          resolve({ type: hdr.type, peerId: hdr.peerId, payload: buf.subarray(HEADER_SIZE, total) });
        }
      }
    };
    sock.on('data', onData);
    sock.once('error', () => { clearTimeout(timer); resolve(null); });
  });
}

async function assertServerStillHealthy(): Promise<void> {
  // A clean handshake must still succeed — proves the server process (and this
  // listener) survived whatever garbage the previous connection(s) threw at it.
  // Reset the shared IP rate limiter first: this file's whole point is throwing
  // enough garbage from 127.0.0.1 to legitimately trip the real S4/S32 IP block
  // (now that server.ts's G5 fix makes it actually accumulate correctly) — that's
  // the rate limiter doing its job, not a sign the server crashed, so it
  // shouldn't fail the "did the process survive" assertion this function makes.
  limiters.rateMap.clear();
  limiters.agentRateMap.clear();
  const sock = await tlsConnect();
  sock.write(buildEnvelope(MessageType.HELLO, 'laptop-01', Buffer.from(JSON.stringify({ version: 1, role: 'sender', agentId: 'laptop-01' }))));
  const challenge = await readFrame(sock);
  expect(challenge?.type).toBe(MessageType.CHALLENGE);

  const hmac = crypto.createHmac('sha256', SECRET).update(challenge!.payload).digest();
  sock.write(buildEnvelope(MessageType.AUTH, 'laptop-01', hmac));
  const result = await readFrame(sock);
  expect(result?.type).toBe(MessageType.AUTH_OK);
  sock.destroy();
}

describe('connection fuzzing — pre-auth', () => {
  it('random bytes before HELLO never crash the server', async () => {
    await fc.assert(
      fc.asyncProperty(fc.uint8Array({ minLength: 0, maxLength: 4096 }), async (bytes) => {
        const sock = await tlsConnect();
        sock.write(Buffer.from(bytes));
        await readFrame(sock, 100); // drain whatever the broker sends back (AUTH_FAIL or nothing), ignore result
        sock.destroy();
      }),
      { numRuns: 25 },
    );
    await assertServerStillHealthy();
  }, 30_000);

  it('a well-formed header with random/oversized LENGTH never crashes the server', async () => {
    await fc.assert(
      fc.asyncProperty(fc.integer({ min: 0, max: 0xffffffff }), async (length) => {
        const header = Buffer.alloc(HEADER_SIZE);
        header[0] = 0x01;
        header[1] = MessageType.HELLO;
        header.writeUInt32BE(length >>> 0, 18);
        const sock = await tlsConnect();
        sock.write(header);
        await readFrame(sock, 100);
        sock.destroy();
      }),
      { numRuns: 25 },
    );
    await assertServerStillHealthy();
  }, 30_000);
});

describe('connection fuzzing — post-auth', () => {
  it('random bytes after a successful handshake never crash the server', async () => {
    let run = 0;
    await fc.assert(
      fc.asyncProperty(fc.uint8Array({ minLength: 0, maxLength: 4096 }), async (bytes) => {
        // Distinct agentId per run, role=receiver (no ALLOWED_SENDERS / agentId
        // rate-limit gate applies to receivers) so repeated auths within this
        // property don't trip the unrelated S4 post-auth rate limiter.
        const agentId = `fuzz-receiver-${run++}`;
        const sock = await tlsConnect();
        sock.write(buildEnvelope(MessageType.HELLO, agentId, Buffer.from(JSON.stringify({ version: 1, role: 'receiver', agentId }))));
        const challenge = await readFrame(sock);
        if (challenge?.type !== MessageType.CHALLENGE) { sock.destroy(); return; }
        const hmac = crypto.createHmac('sha256', SECRET).update(challenge.payload).digest();
        sock.write(buildEnvelope(MessageType.AUTH, agentId, hmac));
        const authResult = await readFrame(sock);
        if (authResult?.type !== MessageType.AUTH_OK) { sock.destroy(); return; }

        sock.write(Buffer.from(bytes));
        await readFrame(sock, 100);
        sock.destroy();
      }),
      { numRuns: 15 },
    );
    await assertServerStillHealthy();
  }, 30_000);
});
