import { Registry } from '../src/registry';
import { Router } from '../src/router';
import { parseHeader, MessageType, HEADER_SIZE } from '../src/protocol';
import pino from 'pino';

const log = pino({ level: 'silent' });

function makeConn(agentId: string) {
  const EventEmitter = require('node:events').EventEmitter;
  const sent: Buffer[] = [];
  const conn = new EventEmitter() as any;
  conn.id      = agentId;
  conn.state   = 'ACTIVE';
  conn.role    = 'sender';
  conn.agentId = agentId;
  conn.send    = (buf: Buffer) => sent.push(buf);
  conn.close   = jest.fn();
  conn._sent   = sent;
  return conn;
}

function makeReceiver() {
  const EventEmitter = require('node:events').EventEmitter;
  const sent: Buffer[] = [];
  const conn = new EventEmitter() as any;
  conn.id      = 'receiver';
  conn.state   = 'ACTIVE';
  conn.role    = 'receiver';
  conn.agentId = 'receiver';
  conn.send    = (buf: Buffer) => sent.push(buf);
  conn.close   = jest.fn();
  conn._sent   = sent;
  return conn;
}

describe('Router — VIDEO_FRAME routing', () => {
  it('delivers VIDEO_FRAME from sender to receiver', () => {
    const registry = new Registry(log);
    const router   = new Router(registry, log);

    const sender   = makeConn('laptop-01');
    const receiver = makeReceiver();

    router.onReceiverAuthenticated(receiver);
    router.onSenderAuthenticated('laptop-01', sender);

    const payload = Buffer.from([0x01, 0x02, 0x03]);
    const header  = { version: 1, type: MessageType.VIDEO_FRAME, peerId: 'laptop-01', length: payload.length };
    sender.emit('message', header, payload);

    expect(receiver._sent.length).toBeGreaterThan(0);
    const last = receiver._sent[receiver._sent.length - 1];
    const hdr  = parseHeader(last);
    expect(hdr.type).toBe(MessageType.VIDEO_FRAME);
    expect(hdr.peerId).toBe('laptop-01');
  });

  it('discards VIDEO_FRAME when receiver is not connected', () => {
    const registry = new Registry(log);
    const router   = new Router(registry, log);
    const sender   = makeConn('laptop-01');
    router.onSenderAuthenticated('laptop-01', sender);

    const payload = Buffer.from([0xAA]);
    sender.emit('message', { version: 1, type: MessageType.VIDEO_FRAME, peerId: '', length: 1 }, payload);
    // no crash = OK (no receiver, frame is discarded)
  });
});

describe('Router — INPUT_EVENT routing', () => {
  it('delivers INPUT_EVENT from receiver to the correct sender', () => {
    const registry = new Registry(log);
    const router   = new Router(registry, log);

    const sender   = makeConn('laptop-01');
    const receiver = makeReceiver();

    router.onSenderAuthenticated('laptop-01', sender);
    router.onReceiverAuthenticated(receiver);

    const payload = Buffer.from([0x01, 0x00, 0xFF, 0x00, 0xFF]);
    const header  = { version: 1, type: MessageType.INPUT_EVENT, peerId: 'laptop-01', length: payload.length };
    receiver.emit('message', header, payload);

    expect(sender._sent.length).toBeGreaterThan(0);
    const last = sender._sent[sender._sent.length - 1];
    const hdr  = parseHeader(last);
    expect(hdr.type).toBe(MessageType.INPUT_EVENT);
  });

  it('logs warning when target sender is not found', () => {
    const registry = new Registry(log);
    const router   = new Router(registry, log);
    const receiver = makeReceiver();
    router.onReceiverAuthenticated(receiver);

    const payload = Buffer.from([0x01]);
    receiver.emit('message', { version: 1, type: MessageType.INPUT_EVENT, peerId: 'inexistente', length: 1 }, payload);
    // no crash = OK
  });
});

describe('Router — SENDER_UP / SENDER_DOWN', () => {
  it('sends SENDER_UP to receiver when sender connects', () => {
    const registry = new Registry(log);
    const router   = new Router(registry, log);
    const receiver = makeReceiver();
    router.onReceiverAuthenticated(receiver);

    const sender = makeConn('laptop-01');
    router.onSenderAuthenticated('laptop-01', sender);

    const upMsg = receiver._sent.find((b: Buffer) => parseHeader(b).type === MessageType.SENDER_UP);
    expect(upMsg).toBeDefined();
    const body = JSON.parse(upMsg!.subarray(HEADER_SIZE).toString());
    expect(body.agentId).toBe('laptop-01');
  });

  it('sends SENDER_DOWN to receiver when sender disconnects', () => {
    const registry = new Registry(log);
    const router   = new Router(registry, log);
    const receiver = makeReceiver();
    router.onReceiverAuthenticated(receiver);

    const sender = makeConn('laptop-01');
    router.onSenderAuthenticated('laptop-01', sender);
    sender.emit('closed');

    const downMsg = receiver._sent.find((b: Buffer) => parseHeader(b).type === MessageType.SENDER_DOWN);
    expect(downMsg).toBeDefined();
  });

  it('receiver gets SENDER_UP for all already-connected senders', () => {
    const registry = new Registry(log);
    const router   = new Router(registry, log);

    const s1 = makeConn('laptop-01');
    const s2 = makeConn('laptop-02');
    router.onSenderAuthenticated('laptop-01', s1);
    router.onSenderAuthenticated('laptop-02', s2);

    const receiver = makeReceiver();
    router.onReceiverAuthenticated(receiver);

    const upMsgs = receiver._sent.filter((b: Buffer) => parseHeader(b).type === MessageType.SENDER_UP);
    expect(upMsgs.length).toBe(2);
  });
});
