import { loadConfig, loadTlsMaterial } from './config.js';
import { warnIfExpiringSoon } from './certExpiry.js';
import { createHealthServer, startHealthListening } from './health.js';
import { createLogger } from './logger.js';
import { MetricsCollector, createMetricsServer, startMetricsListening } from './metrics.js';
import { Registry } from './registry.js';
import { Router } from './router.js';
import { createRateLimiters, createServer, startListening } from './server.js';
import { createWsServer, startWsListening } from './wsServer.js';

async function main(): Promise<void> {
  const cfg = loadConfig();
  const log = createLogger(cfg.logLevel, cfg.logFile, cfg.logMaxSize, cfg.logMaxFiles);

  log.info('SelfDesk Broker iniciando...');

  const tls      = loadTlsMaterial(cfg);
  warnIfExpiringSoon(tls.cert, log);
  const registry = new Registry(log);
  const metrics  = new MetricsCollector();
  const router   = new Router(registry, log, metrics);
  // Shared between the TCP and WSS listeners so a client can't dodge the IP/agentId
  // rate limits by switching transport (see server.ts's RateLimiters doc comment).
  const limiters = createRateLimiters();
  const server   = createServer(cfg, tls, router, log, limiters);

  await startListening(server, cfg.listenPort, log);

  let healthServer: ReturnType<typeof createHealthServer> | undefined;
  if (cfg.healthPort) {
    healthServer = createHealthServer(registry, log);
    await startHealthListening(healthServer, cfg.healthPort, log);
  }

  let metricsServer: ReturnType<typeof createMetricsServer> | undefined;
  if (cfg.metricsPort) {
    metricsServer = createMetricsServer(registry, metrics, log);
    await startMetricsListening(metricsServer, cfg.metricsPort, log);
  }

  let wsServer: ReturnType<typeof createWsServer> | undefined;
  if (cfg.wsPort) {
    wsServer = createWsServer(cfg, tls, router, log, limiters);
    await startWsListening(wsServer, cfg.wsPort, log);
  }

  // .close() stops accepting new connections but its callback only fires once
  // every existing connection ends on its own — a sender streaming VIDEO_FRAME
  // forever never does that by itself, so this is timeout-bounded rather than
  // awaited unconditionally (Sonar-style graceful-shutdown hotspot: stop
  // accepting new work first, but don't hang the process waiting for long-lived
  // streams that were never going to close themselves).
  const closeAsync = (s?: { close(cb: () => void): unknown }): Promise<void> =>
    new Promise((resolve) => {
      if (s) s.close(resolve);
      else resolve();
    });

  const shutdown = async (): Promise<void> => {
    log.info('shutting down...');
    await Promise.race([
      Promise.all([closeAsync(server), closeAsync(healthServer), closeAsync(metricsServer), closeAsync(wsServer)]),
      new Promise<void>((resolve) => setTimeout(resolve, 5000)),
    ]);
    process.exit(0);
  };
  process.on('SIGTERM', () => { void shutdown(); });
  process.on('SIGINT',  () => { void shutdown(); });
}

main().catch((err) => {
  process.stderr.write(`Erro fatal: ${err}\n`);
  process.exit(1);
});
