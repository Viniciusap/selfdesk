import type { Connection } from './connection.js';
import type { Logger } from 'pino';

export class Registry {
  private readonly senders  = new Map<string, Connection>();
  private receiver?: Connection;
  private readonly log: Logger;

  constructor(log: Logger) {
    this.log = log;
  }

  registerSender(agentId: string, conn: Connection): void {
    this.senders.set(agentId, conn);
    this.log.info({ agentId }, 'sender registrado');
    conn.once('closed', () => this.removeSender(agentId));
  }

  registerReceiver(conn: Connection): void {
    this.receiver = conn;
    this.log.info('receiver registrado');
    conn.once('closed', () => {
      this.receiver = undefined;
      this.log.info('receiver desconectado');
    });
  }

  removeSender(agentId: string): void {
    this.senders.delete(agentId);
    this.log.info({ agentId }, 'sender removido');
  }

  getSender(agentId: string): Connection | undefined {
    return this.senders.get(agentId);
  }

  getReceiver(): Connection | undefined {
    return this.receiver;
  }

  getSenderIds(): string[] {
    return Array.from(this.senders.keys());
  }

  hasSender(agentId: string): boolean {
    return this.senders.has(agentId);
  }
}
