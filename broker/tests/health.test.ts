import * as http from 'node:http';
import { Registry } from '../src/registry';
import { createHealthServer, startHealthListening } from '../src/health';
import pino from 'pino';

const log = pino({ level: 'silent' });

function get(port: number, path: string): Promise<{ status: number; body: string }> {
  return new Promise((resolve, reject) => {
    http.get({ host: '127.0.0.1', port, path }, (res) => {
      let body = '';
      res.on('data', (c) => { body += c; });
      res.on('end', () => resolve({ status: res.statusCode ?? 0, body }));
    }).on('error', reject);
  });
}

describe('health endpoint', () => {
  it('reports status, senders, receiver, and uptime on GET /health', async () => {
    const registry = new Registry(log);
    const server   = createHealthServer(registry, log);
    await startHealthListening(server, 0, log);
    const port = (server.address() as { port: number }).port;

    try {
      const { status, body } = await get(port, '/health');
      expect(status).toBe(200);
      const parsed = JSON.parse(body);
      expect(parsed.status).toBe('ok');
      expect(parsed.senders).toEqual([]);
      expect(parsed.receiver).toBe(false);
      expect(typeof parsed.uptime).toBe('number');
    } finally {
      server.close();
    }
  });

  it('returns 404 for unknown paths', async () => {
    const registry = new Registry(log);
    const server   = createHealthServer(registry, log);
    await startHealthListening(server, 0, log);
    const port = (server.address() as { port: number }).port;

    try {
      const { status } = await get(port, '/nope');
      expect(status).toBe(404);
    } finally {
      server.close();
    }
  });
});
