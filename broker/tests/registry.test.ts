import { EventEmitter } from 'node:events';
import { Registry } from '../src/registry';
import pino from 'pino';

const log = pino({ level: 'silent' });

// Minimal Connection fake: an EventEmitter with the fields Registry reads.
function makeConn(state = 'ACTIVE'): any {
  const conn = new EventEmitter() as any;
  conn.state = state;
  conn.mac = undefined;
  conn.senderVersion = undefined;
  conn.close = jest.fn(() => { conn.state = 'CLOSED'; });
  return conn;
}

describe('Registry — sender lifecycle', () => {
  it('closes the previous connection when a sender reconnects with the same id', () => {
    const registry = new Registry(log);
    const first  = makeConn();
    const second = makeConn();

    registry.registerSender('laptop-01', first);
    registry.registerSender('laptop-01', second);

    expect(first.close).toHaveBeenCalledTimes(1);
    expect(registry.getSender('laptop-01')).toBe(second);
  });

  it('does not close a previous connection that is already CLOSED', () => {
    const registry = new Registry(log);
    const first  = makeConn('CLOSED');
    const second = makeConn();

    registry.registerSender('laptop-01', first);
    registry.registerSender('laptop-01', second);

    expect(first.close).not.toHaveBeenCalled();
    expect(registry.getSender('laptop-01')).toBe(second);
  });

  it('a stale closed event from the old connection does not evict the new one (S56)', () => {
    const registry = new Registry(log);
    const first  = makeConn();
    const second = makeConn();

    registry.registerSender('laptop-01', first);
    registry.registerSender('laptop-01', second);
    // The old connection finally emits its delayed 'closed' — must not remove the new entry.
    first.emit('closed');

    expect(registry.hasSender('laptop-01')).toBe(true);
    expect(registry.getSender('laptop-01')).toBe(second);
  });

  it('removes the sender when its own closed event fires', () => {
    const registry = new Registry(log);
    const conn = makeConn();
    registry.registerSender('laptop-01', conn);

    conn.emit('closed');

    expect(registry.hasSender('laptop-01')).toBe(false);
  });

  it('exposes registered senders via getSenderIds and getSenders', () => {
    const registry = new Registry(log);
    const a = makeConn(); a.mac = 'AA'; a.senderVersion = '0.8.3';
    const b = makeConn();
    registry.registerSender('laptop-01', a);
    registry.registerSender('laptop-02', b);

    expect(registry.getSenderIds().sort()).toEqual(['laptop-01', 'laptop-02']);
    const entry = registry.getSenders().find((s) => s.id === 'laptop-01');
    expect(entry).toEqual({ id: 'laptop-01', mac: 'AA', version: '0.8.3' });
  });
});

describe('Registry — receiver lifecycle', () => {
  it('closes the previous receiver when a second one connects', () => {
    const registry = new Registry(log);
    const first  = makeConn();
    const second = makeConn();

    registry.registerReceiver(first);
    registry.registerReceiver(second);

    expect(first.close).toHaveBeenCalledTimes(1);
    expect(registry.getReceiver()).toBe(second);
  });

  it('clears the receiver only when the current one closes', () => {
    const registry = new Registry(log);
    const first  = makeConn();
    const second = makeConn();

    registry.registerReceiver(first);
    registry.registerReceiver(second);
    // Old receiver's delayed 'closed' must not clear the active (second) receiver.
    first.emit('closed');
    expect(registry.getReceiver()).toBe(second);

    second.emit('closed');
    expect(registry.getReceiver()).toBeUndefined();
  });
});
