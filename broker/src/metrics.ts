import * as http from 'node:http';
import type { Registry } from './registry.js';
import type { Logger } from 'pino';

// Issue #19: Prometheus-compatible /metrics endpoint. Plain HTTP, unauthenticated,
// same pattern as the health endpoint (#4) — standard Prometheus scrape convention.
export class MetricsCollector {
  private framesRoutedTotal = 0;
  private bytesRoutedTotal  = 0;
  private readonly startedAt = Date.now();

  recordRoutedFrame(bytes: number): void {
    this.framesRoutedTotal++;
    this.bytesRoutedTotal += bytes;
  }

  render(registry: Registry): string {
    const uptimeSeconds = Math.floor((Date.now() - this.startedAt) / 1000);
    return [
      '# HELP selfdesk_senders_connected Number of currently connected senders',
      '# TYPE selfdesk_senders_connected gauge',
      `selfdesk_senders_connected ${registry.getSenderIds().length}`,
      '',
      '# HELP selfdesk_receiver_connected Whether a receiver is currently connected (0 or 1)',
      '# TYPE selfdesk_receiver_connected gauge',
      `selfdesk_receiver_connected ${registry.getReceiver() ? 1 : 0}`,
      '',
      '# HELP selfdesk_frames_routed_total Total VIDEO_FRAME/AUDIO_FRAME messages routed since startup',
      '# TYPE selfdesk_frames_routed_total counter',
      `selfdesk_frames_routed_total ${this.framesRoutedTotal}`,
      '',
      '# HELP selfdesk_bytes_routed_total Total bytes routed since startup',
      '# TYPE selfdesk_bytes_routed_total counter',
      `selfdesk_bytes_routed_total ${this.bytesRoutedTotal}`,
      '',
      '# HELP selfdesk_broker_uptime_seconds Broker process uptime in seconds',
      '# TYPE selfdesk_broker_uptime_seconds gauge',
      `selfdesk_broker_uptime_seconds ${uptimeSeconds}`,
      '',
    ].join('\n');
  }
}

export function createMetricsServer(registry: Registry, metrics: MetricsCollector, log: Logger): http.Server {
  const server = http.createServer((req, res) => {
    if (req.method !== 'GET' || req.url !== '/metrics') {
      res.writeHead(404, { 'Content-Type': 'text/plain' });
      res.end('not found');
      return;
    }
    res.writeHead(200, { 'Content-Type': 'text/plain; version=0.0.4' });
    res.end(metrics.render(registry));
  });

  server.on('error', (err) => {
    log.error({ err: err.message }, 'metrics server error');
  });

  return server;
}

export function startMetricsListening(server: http.Server, port: number, log: Logger): Promise<void> {
  return new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(port, () => {
      log.info({ port }, 'metrics endpoint listening');
      resolve();
    });
  });
}
