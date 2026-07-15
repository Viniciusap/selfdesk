import * as http from 'node:http';
import type { Registry } from './registry.js';
import type { Logger } from 'pino';

// Issue #4: unauthenticated plain-HTTP health check on a separate port —
// kept off the TLS listener so monitoring tools don't need a client cert/HMAC.
export function createHealthServer(registry: Registry, log: Logger): http.Server {
  const startedAt = Date.now();

  const server = http.createServer((req, res) => {
    if (req.method !== 'GET' || req.url !== '/health') {
      res.writeHead(404, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: 'not found' }));
      return;
    }

    const body = {
      status: 'ok',
      senders: registry.getSenderIds(),
      receiver: registry.getReceiver() !== undefined,
      uptime: Math.floor((Date.now() - startedAt) / 1000),
    };

    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify(body));
  });

  server.on('error', (err) => {
    log.error({ err: err.message }, 'health server error');
  });

  return server;
}

export function startHealthListening(server: http.Server, port: number, log: Logger): Promise<void> {
  return new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(port, () => {
      log.info({ port }, 'health endpoint listening');
      resolve();
    });
  });
}
