import type { Logger } from 'pino';

// Sonar-style dedup: server.ts/health.ts/metrics.ts/wsServer.ts each hand-rolled
// an identical Promise-wrapped server.listen() differing only in the log message.
// tls.Server/http.Server/https.Server all satisfy this minimal shape structurally.
interface Listenable {
  once(event: 'error', cb: (err: Error) => void): unknown;
  listen(port: number, cb: () => void): unknown;
}

export function startListeningOn<T extends Listenable>(
  server: T,
  port: number,
  label: string,
  log: Logger,
): Promise<void> {
  return new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(port, () => {
      log.info({ port }, label);
      resolve();
    });
  });
}
