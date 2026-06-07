import type { Connection } from './connection.js';
import type { Logger } from 'pino';

interface SenderEntry { conn: Connection; mac?: string; version?: string; }

export class Registry {
  private readonly senders  = new Map<string, SenderEntry>();
  private receiver?: Connection;
  private readonly log: Logger;

  constructor(log: Logger) {
    this.log = log;
  }

  registerSender(agentId: string, conn: Connection): void {
    const old = this.senders.get(agentId);
    if (old && old.conn.state !== 'CLOSED') {
      this.log.warn({ agentId }, 'sender reconnecting — closing previous connection');
      old.conn.close();
    }
    this.senders.set(agentId, { conn, mac: conn.mac, version: conn.senderVersion });
    this.log.info({ agentId }, 'sender registered');
    // S56: check ref identity so a stale 'closed' event from the old conn doesn't evict the new one
    conn.once('closed', () => {
      if (this.senders.get(agentId)?.conn === conn) this.removeSender(agentId);
    });
  }

  registerReceiver(conn: Connection): void {
    if (this.receiver && this.receiver.state !== 'CLOSED') {
      this.log.warn('second receiver connected — closing previous receiver');
      this.receiver.close();
    }
    this.receiver = conn;
    this.log.info('receiver registered');
    conn.once('closed', () => {
      if (this.receiver === conn) {
        this.receiver = undefined;
        this.log.info('receiver disconnected');
      }
    });
  }

  removeSender(agentId: string): void {
    this.senders.delete(agentId);
    this.log.info({ agentId }, 'sender removed');
  }

  getSender(agentId: string): Connection | undefined {
    return this.senders.get(agentId)?.conn;
  }

  getReceiver(): Connection | undefined {
    return this.receiver;
  }

  getSenderIds(): string[] {
    return Array.from(this.senders.keys());
  }

  getSenders(): { id: string; mac?: string; version?: string }[] {
    return Array.from(this.senders.entries()).map(([id, e]) => ({ id, mac: e.mac, version: e.version }));
  }

  hasSender(agentId: string): boolean {
    return this.senders.has(agentId);
  }
}
