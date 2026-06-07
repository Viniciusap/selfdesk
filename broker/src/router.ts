import {
  MessageType,
  ParsedHeader,
  build,
  buildEnvelope,
} from './protocol.js';
import type { Registry } from './registry.js';
import type { Connection } from './connection.js';
import type { Logger } from 'pino';

// Types routed from sender to receiver (in addition to VIDEO_FRAME and AUDIO_FRAME)
const SENDER_TO_RECEIVER_TYPES = new Set<number>([
  MessageType.CLIPBOARD,
  MessageType.FILE_HEADER,
  MessageType.FILE_CHUNK,
  MessageType.FILE_DONE,
  MessageType.FILE_ERROR,
  MessageType.MONITOR_LIST,
]);

// Types routed from receiver to a specific target sender (or broadcast)
const RECEIVER_TO_SENDER_FILE_TYPES = new Set<number>([
  MessageType.CLIPBOARD,
  MessageType.FILE_HEADER,
  MessageType.FILE_CHUNK,
  MessageType.FILE_DONE,
  MessageType.FILE_ERROR,
]);

export class Router {
  private readonly registry: Registry;
  private readonly log: Logger;

  constructor(registry: Registry, log: Logger) {
    this.registry = registry;
    this.log      = log;
  }

  onSenderAuthenticated(agentId: string, conn: Connection): void {
    this.registry.registerSender(agentId, conn);

    const receiver = this.registry.getReceiver();
    if (receiver) {
      receiver.send(build.senderUp(agentId, conn.mac, conn.senderVersion));
    }

    conn.on('message', (header: ParsedHeader, payload: Buffer) =>
      this.routeSenderMessage(agentId, header, payload),
    );

    conn.once('closed', () => {
      const rx = this.registry.getReceiver();
      if (rx) rx.send(build.senderDown(agentId));
    });
  }

  onReceiverAuthenticated(conn: Connection): void {
    this.registry.registerReceiver(conn);

    for (const { id, mac, version } of this.registry.getSenders()) {
      conn.send(build.senderUp(id, mac, version));
    }

    conn.on('message', (header: ParsedHeader, payload: Buffer) =>
      this.routeReceiverMessage(header, payload),
    );
  }

  private routeSenderMessage(
    agentId: string,
    header: ParsedHeader,
    payload: Buffer,
  ): void {
    const receiver = this.registry.getReceiver();
    if (!receiver) return;

    if (header.type === MessageType.VIDEO_FRAME || header.type === MessageType.AUDIO_FRAME) {
      // S58: drop frame under backpressure to prevent broker OOM
      if (receiver.writableLength > 32 * 1024 * 1024) return;
      receiver.send(buildEnvelope(header.type, agentId, payload));
      return;
    }

    if (SENDER_TO_RECEIVER_TYPES.has(header.type)) {
      receiver.send(buildEnvelope(header.type, agentId, payload));
      return;
    }

    this.log.debug({ type: header.type, agentId }, 'unknown sender message type — ignored');
  }

  private routeReceiverMessage(header: ParsedHeader, payload: Buffer): void {
    if (header.type === MessageType.INPUT_EVENT    ||
        header.type === MessageType.REQUEST_IDR    ||
        header.type === MessageType.SELECT_MONITOR ||
        header.type === MessageType.REMOTE_REBOOT) {
      const targetId = header.peerId;
      const sender   = this.registry.getSender(targetId);
      if (!sender) {
        this.log.warn({ targetId, type: header.type }, 'target sender not found');
        return;
      }
      sender.send(buildEnvelope(header.type, targetId, payload));
      return;
    }
    if (RECEIVER_TO_SENDER_FILE_TYPES.has(header.type)) {
      // S57: require explicit PEER_ID for file transfers — no broadcast
      const targetId = header.peerId;
      if (!targetId) {
        this.log.warn({ type: header.type }, 'file message without target PEER_ID — discarded');
        return;
      }
      const sender = this.registry.getSender(targetId);
      if (!sender) {
        this.log.warn({ targetId, type: header.type }, 'target sender not found');
        return;
      }
      sender.send(buildEnvelope(header.type, targetId, payload));
      return;
    }
    this.log.debug({ type: header.type }, 'unknown receiver message type — ignored');
  }
}
