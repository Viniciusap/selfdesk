import {
  MessageType,
  ParsedHeader,
  build,
  buildEnvelope,
} from './protocol.js';
import type { Registry } from './registry.js';
import type { Connection } from './connection.js';
import type { Logger } from 'pino';

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
      receiver.send(build.senderUp(agentId));
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

    for (const id of this.registry.getSenderIds()) {
      conn.send(build.senderUp(id));
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
    if (header.type === MessageType.VIDEO_FRAME) {
      const receiver = this.registry.getReceiver();
      if (!receiver) return;
      receiver.send(buildEnvelope(header.type, agentId, payload));
      return;
    }
    if (header.type === MessageType.CLIPBOARD ||
        header.type === MessageType.FILE_HEADER ||
        header.type === MessageType.FILE_CHUNK  ||
        header.type === MessageType.FILE_DONE   ||
        header.type === MessageType.FILE_ERROR) {
      const receiver = this.registry.getReceiver();
      if (!receiver) return;
      receiver.send(buildEnvelope(header.type, agentId, payload));
      return;
    }
    this.log.debug({ type: header.type, agentId }, 'mensagem de sender ignorada');
  }

  private routeReceiverMessage(header: ParsedHeader, payload: Buffer): void {
    if (header.type === MessageType.INPUT_EVENT) {
      const targetId = header.peerId;
      const sender   = this.registry.getSender(targetId);
      if (!sender) {
        this.log.warn({ targetId }, 'sender alvo não encontrado para INPUT_EVENT');
        return;
      }
      sender.send(buildEnvelope(header.type, targetId, payload));
      return;
    }
    if (header.type === MessageType.CLIPBOARD ||
        header.type === MessageType.FILE_HEADER ||
        header.type === MessageType.FILE_CHUNK  ||
        header.type === MessageType.FILE_DONE   ||
        header.type === MessageType.FILE_ERROR) {
      const targetId = header.peerId;
      const sender   = targetId ? this.registry.getSender(targetId) : undefined;
      if (sender) {
        sender.send(buildEnvelope(header.type, targetId, payload));
      } else {
        for (const id of this.registry.getSenderIds()) {
          this.registry.getSender(id)?.send(buildEnvelope(header.type, id, payload));
        }
      }
      return;
    }
    this.log.debug({ type: header.type }, 'mensagem de receiver ignorada');
  }
}
