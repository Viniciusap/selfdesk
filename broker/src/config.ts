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
  healthPort?:   number;
}

export function loadConfig(envFile?: string): BrokerConfig {
  const file = envFile ?? path.join(__dirname, '..', '.env');
  dotenvConfig({ path: file });

  const port = parseInt(process.env.LISTEN_PORT ?? '7000', 10);
  if (isNaN(port) || port < 1 || port > 65535) {
    throw new Error(`Invalid LISTEN_PORT: ${process.env.LISTEN_PORT}`);
  }

  const secret = process.env.SHARED_SECRET;
  if (!secret || secret.length < 32) {
    throw new Error('SHARED_SECRET missing or too short (minimum 32 characters)');
  }

  const senders = (process.env.ALLOWED_SENDERS ?? '')
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean);

  const certPath = process.env.TLS_CERT_PATH;
  const keyPath  = process.env.TLS_KEY_PATH;
  if (!certPath || !keyPath) {
    throw new Error('TLS_CERT_PATH and TLS_KEY_PATH are required');
  }

  const envDir = path.resolve(path.dirname(file));
  const resolvedCert = path.resolve(envDir, certPath);
  const resolvedKey  = path.resolve(envDir, keyPath);

  // M6: resolved paths must stay within envDir — reject '../..' escapes via TLS_CERT_PATH/TLS_KEY_PATH
  const envDirWithSep = envDir.endsWith(path.sep) ? envDir : envDir + path.sep;
  for (const [label, p] of [['TLS_CERT_PATH', resolvedCert], ['TLS_KEY_PATH', resolvedKey]] as const) {
    if (!p.startsWith(envDirWithSep)) {
      throw new Error(`${label} '${p}' resolves outside the .env directory (${envDir})`);
    }
  }

  // S27: verify resolved paths are regular files, not devices or FIFOs
  for (const [label, p] of [['TLS_CERT_PATH', resolvedCert], ['TLS_KEY_PATH', resolvedKey]] as const) {
    if (!fs.existsSync(p) || !fs.statSync(p).isFile()) {
      throw new Error(`${label} '${p}' does not exist or is not a regular file`);
    }
  }

  let healthPort: number | undefined;
  if (process.env.HEALTH_PORT) {
    healthPort = parseInt(process.env.HEALTH_PORT, 10);
    if (isNaN(healthPort) || healthPort < 1 || healthPort > 65535) {
      throw new Error(`Invalid HEALTH_PORT: ${process.env.HEALTH_PORT}`);
    }
  }

  return {
    listenPort:     port,
    sharedSecret:  secret,
    allowedSenders: new Set(senders),
    tlsCertPath:   resolvedCert,
    tlsKeyPath:    resolvedKey,
    logLevel:      process.env.LOG_LEVEL ?? 'info',
    healthPort,
  };
}

export function loadTlsMaterial(cfg: BrokerConfig): { cert: Buffer; key: Buffer } {
  // S29: warn if TLS private key is group/world-readable on Unix
  if (process.platform !== 'win32') {
    const stat = fs.statSync(cfg.tlsKeyPath);
    if ((stat.mode & 0o077) !== 0) {
      throw new Error(
        `TLS private key '${cfg.tlsKeyPath}' must be accessible only by the owner (chmod 600)`,
      );
    }
  }
  return {
    cert: fs.readFileSync(cfg.tlsCertPath),
    key:  fs.readFileSync(cfg.tlsKeyPath),
  };
}
