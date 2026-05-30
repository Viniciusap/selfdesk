import { loadConfig, loadTlsMaterial } from './config.js';
import { createLogger } from './logger.js';
import { Registry } from './registry.js';
import { Router } from './router.js';
import { createServer, startListening } from './server.js';

async function main(): Promise<void> {
  const cfg = loadConfig();
  const log = createLogger(cfg.logLevel);

  log.info('SelfDesk Broker iniciando...');

  const tls      = loadTlsMaterial(cfg);
  const registry = new Registry(log);
  const router   = new Router(registry, log);
  const server   = createServer(cfg, tls, router, log);

  await startListening(server, cfg.listenPort, log);

  process.on('SIGTERM', () => { server.close(); process.exit(0); });
  process.on('SIGINT',  () => { server.close(); process.exit(0); });
}

main().catch((err) => {
  process.stderr.write(`Erro fatal: ${err}\n`);
  process.exit(1);
});
