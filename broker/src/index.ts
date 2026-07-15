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

  const shutdown = () => {
    server.close(); healthServer?.close(); metricsServer?.close(); wsServer?.close();
    process.exit(0);
  };
  process.on('SIGTERM', shutdown);
  process.on('SIGINT',  shutdown);
}

main().catch((err) => {
  process.stderr.write(`Erro fatal: ${err}\n`);
  process.exit(1);
});
