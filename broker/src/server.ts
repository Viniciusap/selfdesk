import * as tls from 'node:tls';
import type { Logger } from 'pino';
import type { BrokerConfig } from './config.js';
import { Connection } from './connection.js';
import { startListeningOn } from './listen.js';
import { Router } from './router.js';

const FAIL_LIMIT   = 5;
const FAIL_WINDOW  = 60_000;   // 1 min
const BLOCK_PERIOD = 5 * 60_000; // 5 min

// S4: rate limit successful (re)authentications by agentId, not just failures by IP.
// A leaked or misbehaving sender credential can hop IPs (NAT, DHCP) but not agentId.
const AGENT_AUTH_LIMIT  = 10;
const AGENT_AUTH_WINDOW = 60_000; // 1 min

interface RateEntry { count: number; windowStart: number; blockedUntil: number }

// Issue #12: shared between the TLS TCP listener (server.ts) and the WSS listener
// (wsServer.ts) so a client can't dodge rate limiting by switching transport —
// both funnel into the same Connection/Router pipeline and the same rate maps.
export interface RateLimiters {
  rateMap: Map<string, RateEntry>;
  agentRateMap: Map<string, RateEntry>;
}

export function createRateLimiters(): RateLimiters {
  return { rateMap: new Map(), agentRateMap: new Map() };
}

// True if this IP is currently blocked from a prior burst of failed auths.
// Purges expired entries opportunistically to keep the maps from growing unbounded.
export function isIpBlocked(limiters: RateLimiters, ip: string): boolean {
  const now = Date.now();
  for (const [k, v] of limiters.rateMap) {
    if (now > v.blockedUntil + FAIL_WINDOW) limiters.rateMap.delete(k);
  }
  for (const [k, v] of limiters.agentRateMap) {
    if (now - v.windowStart > AGENT_AUTH_WINDOW) limiters.agentRateMap.delete(k);
  }
  const entry = limiters.rateMap.get(ip);
  return entry !== undefined && now < entry.blockedUntil;
}

export function attachConnectionHandlers(
  conn: Connection,
  ip: string,
  router: Router,
  limiters: RateLimiters,
  log: Logger,
): void {
  let authed = false;

  conn.on('authenticated', () => { authed = true; });

  conn.on('closed', () => {
    if (authed) return;
    const t   = Date.now();
    const rec = limiters.rateMap.get(ip) ?? { count: 0, windowStart: t, blockedUntil: 0 };
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
    limiters.rateMap.set(ip, rec);
  });

  conn.on('authenticated', (role, agentId) => {
    if (role === 'sender') {
      const t   = Date.now();
      const rec = limiters.agentRateMap.get(agentId) ?? { count: 0, windowStart: t, blockedUntil: 0 };
      if (t - rec.windowStart > AGENT_AUTH_WINDOW) {
        rec.count = 1;
        rec.windowStart = t;
      } else {
        rec.count++;
      }
      limiters.agentRateMap.set(agentId, rec);

      if (rec.count > AGENT_AUTH_LIMIT) {
        log.warn({ agentId, count: rec.count }, 'agentId exceeded post-auth rate limit — dropping connection');
        conn.close();
        return;
      }
      router.onSenderAuthenticated(agentId, conn);
    } else {
      router.onReceiverAuthenticated(conn);
    }
  });

  conn.on('error', (err) => {
    log.warn({ err: err.message }, 'connection error');
  });
}

export function createServer(
  cfg: BrokerConfig,
  tlsMaterial: { cert: Buffer; key: Buffer },
  router: Router,
  log: Logger,
  limiters: RateLimiters = createRateLimiters(),
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
      const ip = socket.remoteAddress ?? 'unknown';

      if (isIpBlocked(limiters, ip)) {
        socket.destroy();
        return;
      }

      const conn = new Connection(socket, cfg.sharedSecret, cfg.allowedSenders, log);
      attachConnectionHandlers(conn, ip, router, limiters, log);
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
  return startListeningOn(server, port, 'broker escutando', log);
}
