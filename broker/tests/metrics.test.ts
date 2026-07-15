import * as http from 'node:http';
import { Registry } from '../src/registry';
import { MetricsCollector, createMetricsServer, startMetricsListening } from '../src/metrics';
import pino from 'pino';

const log = pino({ level: 'silent' });

function get(port: number, path: string): Promise<{ status: number; body: string }> {
  return new Promise((resolve, reject) => {
    http.get({ host: '127.0.0.1', port, path }, (res) => {
      let body = '';
      res.on('data', (c) => { body += c; });
      res.on('end', () => resolve({ status: res.statusCode ?? 0, body }));
    }).on('error', reject);
  });
}

describe('metrics endpoint', () => {
  it('exposes Prometheus text format on GET /metrics', async () => {
    const registry = new Registry(log);
    const metrics  = new MetricsCollector();
    metrics.recordRoutedFrame(1024);
    metrics.recordRoutedFrame(2048);

    const server = createMetricsServer(registry, metrics, log);
    await startMetricsListening(server, 0, log);
    const port = (server.address() as { port: number }).port;

    try {
      const { status, body } = await get(port, '/metrics');
      expect(status).toBe(200);
      expect(body).toContain('# TYPE selfdesk_senders_connected gauge');
      expect(body).toContain('selfdesk_senders_connected 0');
      expect(body).toContain('selfdesk_receiver_connected 0');
      expect(body).toContain('selfdesk_frames_routed_total 2');
      expect(body).toContain('selfdesk_bytes_routed_total 3072');
      expect(body).toMatch(/selfdesk_broker_uptime_seconds \d+/);
    } finally {
      server.close();
    }
  });

  it('returns 404 for unknown paths', async () => {
    const registry = new Registry(log);
    const metrics  = new MetricsCollector();
    const server   = createMetricsServer(registry, metrics, log);
    await startMetricsListening(server, 0, log);
    const port = (server.address() as { port: number }).port;

    try {
      const { status } = await get(port, '/nope');
      expect(status).toBe(404);
    } finally {
      server.close();
    }
  });
});
