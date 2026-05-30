import * as tls from 'node:tls';
import type { Logger } from 'pino';
import type { BrokerConfig } from './config.js';
import { Connection } from './connection.js';
import { Router } from './router.js';

export function createServer(
  cfg: BrokerConfig,
  tlsMaterial: { cert: Buffer; key: Buffer },
  router: Router,
  log: Logger,
): tls.Server {
  const server = tls.createServer(
    {
      cert:               tlsMaterial.cert,
      key:                tlsMaterial.key,
      requestCert:        false,
      rejectUnauthorized: false,
      minVersion:         'TLSv1.3',
    },
    (socket) => {
      const conn = new Connection(socket, cfg.sharedSecret, cfg.allowedSenders, log);

      conn.on('authenticated', (role, agentId) => {
        if (role === 'sender') {
          router.onSenderAuthenticated(agentId, conn);
        } else {
          router.onReceiverAuthenticated(conn);
        }
      });

      conn.on('error', (err) => {
        log.warn({ err: err.message }, 'erro de conexão');
      });
    },
  );

  server.on('error', (err) => {
    log.error({ err: err.message }, 'erro no servidor TLS');
  });

  return server;
}

export function startListening(
  server: tls.Server,
  port: number,
  log: Logger,
): Promise<void> {
  return new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(port, () => {
      log.info({ port }, 'broker escutando');
      resolve();
    });
  });
}
