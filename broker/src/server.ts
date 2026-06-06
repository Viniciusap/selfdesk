import * as tls from 'node:tls';
import type { Logger } from 'pino';
import type { BrokerConfig } from './config.js';
import { Connection } from './connection.js';
import { Router } from './router.js';

const FAIL_LIMIT   = 5;
const FAIL_WINDOW  = 60_000;   // 1 min
const BLOCK_PERIOD = 5 * 60_000; // 5 min

interface RateEntry { count: number; windowStart: number; blockedUntil: number }

export function createServer(
  cfg: BrokerConfig,
  tlsMaterial: { cert: Buffer; key: Buffer },
  router: Router,
  log: Logger,
): tls.Server {
  const rateMap = new Map<string, RateEntry>();

  const server = tls.createServer(
    {
      cert:               tlsMaterial.cert,
      key:                tlsMaterial.key,
      requestCert:        false,
      rejectUnauthorized: false,
      minVersion:         'TLSv1.3',
    },
    (socket) => {
      const ip  = socket.remoteAddress ?? 'unknown';
      const now = Date.now();

      // Purge expired entries to prevent unbounded growth
      for (const [k, v] of rateMap) {
        if (now > v.blockedUntil + FAIL_WINDOW) rateMap.delete(k);
      }

      const entry = rateMap.get(ip);
      if (entry && now < entry.blockedUntil) {
        socket.destroy();
        return;
      }

      const conn = new Connection(socket, cfg.sharedSecret, cfg.allowedSenders, log);
      let authed = false;

      conn.on('authenticated', () => { authed = true; });

      conn.on('closed', () => {
        if (authed) return;
        const t   = Date.now();
        const rec = rateMap.get(ip) ?? { count: 0, windowStart: t, blockedUntil: 0 };
        if (t - rec.windowStart > FAIL_WINDOW) {
          rec.count = 1;
          rec.windowStart = t;
          rec.blockedUntil = 0;
        } else {
          rec.count++;
          if (rec.count >= FAIL_LIMIT) {
            rec.blockedUntil = t + BLOCK_PERIOD;
            log.warn({ ip, count: rec.count }, 'IP bloqueado por falhas repetidas de auth');
          }
        }
        rateMap.set(ip, rec);
      });

      conn.on('authenticated', (role, agentId) => {
        if (role === 'sender') {
          router.onSenderAuthenticated(agentId, conn);
        } else {
          router.onReceiverAuthenticated(conn);
        }
      });

      conn.on('error', (err) => {
        log.warn({ err: err.message }, 'connection error');
      });
    },
  );

  server.on('error', (err) => {
    log.error({ err: err.message }, 'TLS server error');
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
