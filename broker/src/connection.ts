import { EventEmitter } from 'node:events';
import * as tls from 'node:tls';
import {
  HANDSHAKE_TIMEOUT_MS,
  HEADER_SIZE,
  MAX_FRAME_BYTES,
  PING_INTERVAL_MS,
  PONG_TIMEOUT_MS,
  MessageType,
  ParsedHeader,
  build,
  generateNonce,
  parseHeader,
  verifyHmac,
} from './protocol.js';
import type { Logger } from 'pino';

export type ConnState =
  | 'CONNECTING'
  | 'HELLO_RECEIVED'
  | 'CHALLENGE_SENT'
  | 'ACTIVE'
  | 'CLOSED';

export type Role = 'sender' | 'receiver';

export interface HelloPayload {
  version:       number;
  role:          Role;
  agentId:       string;
  mac?:          string;
  senderVersion?: string;
}

export interface ConnectionEvents {
  message:     [header: ParsedHeader, payload: Buffer];
  authenticated: [role: Role, agentId: string];
  closed:      [];
  error:       [err: Error];
}

export class Connection extends EventEmitter {
  readonly id: string;
  state: ConnState = 'CONNECTING';
  role?: Role;
  agentId?: string;
  mac?: string;
  senderVersion?: string;

  private readonly socket: tls.TLSSocket;
  private readonly secret: string;
  private readonly allowedSenders: Set<string>;
  private readonly log: Logger;

  private chunks: Buffer[] = [];
  private chunksLen = 0;
  private nonce?: Buffer;
  private pingTimer?: NodeJS.Timeout;
  private pongTimer?: NodeJS.Timeout;
  private handshakeTimer?: NodeJS.Timeout;

  constructor(
    socket: tls.TLSSocket,
    secret: string,
    allowedSenders: Set<string>,
    log: Logger,
  ) {
    super();
    this.socket         = socket;
    this.secret         = secret;
    this.allowedSenders = allowedSenders;
    this.log            = log;
    this.id             = `conn-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;

    socket.on('data',  (chunk: Buffer) => this.onData(chunk));
    socket.on('end',   () => this.close());
    socket.on('error', (err) => { this.emit('error', err); this.close(); });

    this.handshakeTimer = setTimeout(() => {
      if (this.state !== 'ACTIVE' && this.state !== 'CLOSED') {
        this.log.warn({ id: this.id }, 'handshake timeout — closing connection');
        this.close();
      }
    }, HANDSHAKE_TIMEOUT_MS);
    this.handshakeTimer.unref?.();
  }

  send(buf: Buffer): void {
    if (this.state !== 'CLOSED') {
      this.socket.write(buf);
    }
  }

  close(): void {
    if (this.state === 'CLOSED') return;
    this.state = 'CLOSED';
    clearTimeout(this.handshakeTimer);
    clearTimeout(this.pingTimer);
    clearTimeout(this.pongTimer);
    this.socket.destroy();
    this.emit('closed');
  }

  private onData(chunk: Buffer): void {
    this.chunks.push(chunk);
    this.chunksLen += chunk.length;
    this.drainFrames();
  }

  private consolidate(): Buffer {
    if (this.chunks.length === 1) return this.chunks[0];
    const flat = Buffer.concat(this.chunks, this.chunksLen);
    this.chunks = [flat];
    return flat;
  }

  private drainFrames(): void {
    while (this.chunksLen >= HEADER_SIZE) {
      const buf    = this.consolidate();
      const header = parseHeader(buf);

      if (header.length > MAX_FRAME_BYTES) {
        this.log.warn({ id: this.id, length: header.length }, 'frame oversized — closing connection');
        this.close();
        return;
      }

      const total = HEADER_SIZE + header.length;
      if (this.chunksLen < total) break;

      const payload   = buf.subarray(HEADER_SIZE, total);
      const remainder = buf.subarray(total);
      this.chunks     = remainder.length > 0 ? [remainder] : [];
      this.chunksLen  = remainder.length;
      this.handleFrame(header, payload);
    }
  }

  private handleFrame(header: ParsedHeader, payload: Buffer): void {
    if (this.state !== 'ACTIVE') {
      this.handleHandshake(header, payload);
      return;
    }

    switch (header.type) {
      case MessageType.PING:
        this.send(build.pong(payload));
        break;
      case MessageType.PONG:
        clearTimeout(this.pongTimer);
        break;
      case MessageType.BYE:
        this.close();
        break;
      default:
        this.emit('message', header, payload);
    }
  }

  private handleHandshake(header: ParsedHeader, payload: Buffer): void {
    switch (this.state) {
      case 'CONNECTING': {
        if (header.type !== MessageType.HELLO) {
          this.rejectAuth('Expected HELLO');
          return;
        }
        let hello: HelloPayload;
        try {
          hello = JSON.parse(payload.toString('utf8')) as HelloPayload;
        } catch {
          this.rejectAuth('Invalid HELLO payload');
          return;
        }

        // S6: protocol constraint — agentId must fit in 16-byte PEER_ID field
        if (typeof hello.agentId === 'string' && hello.agentId.length > 16) {
          this.rejectAuth('agentId exceeds 16 characters');
          return;
        }

        this.role          = hello.role;
        this.agentId       = hello.agentId;
        // S59: validate MAC format; ignore if malformed
        const macRegex = /^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$/;
        this.mac           = hello.mac && macRegex.test(hello.mac) ? hello.mac : undefined;
        this.senderVersion = hello.senderVersion;
        this.state   = 'HELLO_RECEIVED';

        this.nonce = generateNonce();
        this.state = 'CHALLENGE_SENT';
        this.send(build.challenge(this.nonce));
        break;
      }

      case 'CHALLENGE_SENT': {
        if (header.type !== MessageType.AUTH) {
          this.rejectAuth('Expected AUTH');
          return;
        }
        if (payload.length !== 32) {
          this.rejectAuth('AUTH payload must be 32 bytes');
          return;
        }
        if (!this.nonce || !verifyHmac(this.nonce, payload, this.secret)) {
          this.rejectAuth('Invalid HMAC — wrong secret');
          return;
        }
        // S26: ALLOWED_SENDERS check deferred here so all senders receive CHALLENGE
        if (this.role === 'sender' && !this.allowedSenders.has(this.agentId!)) {
          this.rejectAuth(`agentId '${this.agentId}' is not in ALLOWED_SENDERS`);
          return;
        }

        this.state = 'ACTIVE';
        clearTimeout(this.handshakeTimer);
        this.send(build.authOk(this.agentId ?? ''));
        this.log.info({ agentId: this.agentId, role: this.role }, 'authenticated');
        this.emit('authenticated', this.role!, this.agentId!);
        this.startHeartbeat();
        break;
      }

      default:
        this.rejectAuth('Unexpected handshake sequence');
    }
  }

  private rejectAuth(reason: string): void {
    this.log.warn({ reason, id: this.id }, 'auth failed');
    // S32: send generic reason to client; detail stays in server log only
    this.send(build.authFail(this.agentId ?? '', 'Authentication failed'));
    this.close();
  }

  private startHeartbeat(): void {
    this.pingTimer = setInterval(() => {
      if (this.state !== 'ACTIVE') return;
      clearTimeout(this.pongTimer);
      this.send(build.ping(BigInt(Date.now())));

      this.pongTimer = setTimeout(() => {
        this.log.warn({ agentId: this.agentId }, 'PONG timeout — closing');
        this.close();
      }, PONG_TIMEOUT_MS);
    }, PING_INTERVAL_MS);

    this.pingTimer.unref?.();
  }
}
