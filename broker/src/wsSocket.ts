import { EventEmitter } from 'node:events';
import type WebSocket from 'ws';
import type { FramedSocket } from './connection.js';

// Issue #12: adapts a `ws` WebSocket to the same five-member FramedSocket surface
// tls.TLSSocket already satisfies, so Connection/Router/Registry stay transport-agnostic
// and unchanged — the web viewer authenticates and routes through the exact same
// HELLO/CHALLENGE/AUTH state machine and MAX_FRAME_BYTES/backpressure guards as the
// native TCP viewer.
export class WsFramedSocket extends EventEmitter implements FramedSocket {
  constructor(private readonly ws: WebSocket) {
    super();

    ws.on('message', (data: WebSocket.RawData, isBinary: boolean) => {
      if (!isBinary) {
        // Wire protocol is binary-only — a text frame is not a client we speak to.
        this.destroy();
        return;
      }
      this.emit('data', WsFramedSocket.toBuffer(data));
    });

    ws.on('close', () => this.emit('end'));
    ws.on('error', (err: Error) => this.emit('error', err));
  }

  private static toBuffer(data: WebSocket.RawData): Buffer {
    if (Buffer.isBuffer(data)) return data;
    if (Array.isArray(data)) return Buffer.concat(data);
    return Buffer.from(data);
  }

  get writableLength(): number {
    return this.ws.bufferedAmount;
  }

  write(buf: Buffer): void {
    if (this.ws.readyState === this.ws.OPEN) this.ws.send(buf);
  }

  destroy(): void {
    this.ws.terminate();
  }
}
