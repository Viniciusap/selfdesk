import { config as dotenvConfig } from 'dotenv';
import * as path from 'node:path';
import * as fs from 'node:fs';

export interface BrokerConfig {
  listenPort:     number;
  sharedSecret:  string;
  allowedSenders: Set<string>;
  tlsCertPath:   string;
  tlsKeyPath:    string;
  logLevel:      string;
}

export function loadConfig(envFile?: string): BrokerConfig {
  const file = envFile ?? path.join(__dirname, '..', '.env');
  dotenvConfig({ path: file });

  const port = parseInt(process.env.LISTEN_PORT ?? '7000', 10);
  if (isNaN(port) || port < 1 || port > 65535) {
    throw new Error(`LISTEN_PORT inválida: ${process.env.LISTEN_PORT}`);
  }

  const secret = process.env.SHARED_SECRET;
  if (!secret || secret.length < 8) {
    throw new Error('SHARED_SECRET ausente ou muito curto');
  }

  const senders = (process.env.ALLOWED_SENDERS ?? '')
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean);

  const certPath = process.env.TLS_CERT_PATH;
  const keyPath  = process.env.TLS_KEY_PATH;
  if (!certPath || !keyPath) {
    throw new Error('TLS_CERT_PATH e TLS_KEY_PATH são obrigatórios');
  }

  return {
    listenPort:     port,
    sharedSecret:  secret,
    allowedSenders: new Set(senders),
    tlsCertPath:   path.resolve(path.dirname(file), '..', certPath.replace(/^\.\.\//, '')),
    tlsKeyPath:    path.resolve(path.dirname(file), '..', keyPath.replace(/^\.\.\//, '')),
    logLevel:      process.env.LOG_LEVEL ?? 'info',
  };
}

export function loadTlsMaterial(cfg: BrokerConfig): { cert: Buffer; key: Buffer } {
  return {
    cert: fs.readFileSync(cfg.tlsCertPath),
    key:  fs.readFileSync(cfg.tlsKeyPath),
  };
}
