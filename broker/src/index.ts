import { loadConfig, loadTlsMaterial } from './config.js';
import { warnIfExpiringSoon } from './certExpiry.js';
import { createHealthServer, startHealthListening } from './health.js';
import { createLogger } from './logger.js';
import { Registry } from './registry.js';
import { Router } from './router.js';
import { createServer, startListening } from './server.js';

async function main(): Promise<void> {
  const cfg = loadConfig();
  const log = createLogger(cfg.logLevel, cfg.logFile, cfg.logMaxSize, cfg.logMaxFiles);

  log.info('SelfDesk Broker iniciando...');

  const tls      = loadTlsMaterial(cfg);
  warnIfExpiringSoon(tls.cert, log);
  const registry = new Registry(log);
  const router   = new Router(registry, log);
  const server   = createServer(cfg, tls, router, log);

  await startListening(server, cfg.listenPort, log);

  let healthServer: ReturnType<typeof createHealthServer> | undefined;
  if (cfg.healthPort) {
    healthServer = createHealthServer(registry, log);
    await startHealthListening(healthServer, cfg.healthPort, log);
  }

  process.on('SIGTERM', () => { server.close(); healthServer?.close(); process.exit(0); });
  process.on('SIGINT',  () => { server.close(); healthServer?.close(); process.exit(0); });
}

main().catch((err) => {
  process.stderr.write(`Erro fatal: ${err}\n`);
  process.exit(1);
});
