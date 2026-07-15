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
  logFile?:      string;
  logMaxSize:    string;
  logMaxFiles:   number;
  metricsPort?:  number;
  wsPort?:       number;
}

// Sonar-style dedup: LISTEN_PORT/HEALTH_PORT/METRICS_PORT/WS_PORT each used to
// hand-roll the same parseInt + isNaN + range-check + throw block.
function parsePort(envValue: string | undefined, label: string): number | undefined {
  if (!envValue) return undefined;
  const port = parseInt(envValue, 10);
  if (isNaN(port) || port < 1 || port > 65535) {
    throw new Error(`Invalid ${label}: ${envValue}`);
  }
  return port;
}

export function loadConfig(envFile?: string): BrokerConfig {
  const file = envFile ?? path.join(__dirname, '..', '.env');
  dotenvConfig({ path: file });

  const port = parsePort(process.env.LISTEN_PORT ?? '7000', 'LISTEN_PORT') ?? 7000;

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

  // M6: resolved paths must stay within the repo root (envDir's parent) — reject deep
  // '../../..' escapes via TLS_CERT_PATH/TLS_KEY_PATH while still allowing the standard
  // '../certs/...' convention used by every .env.example in this repo (certs/ is a
  // sibling directory of broker/, one level up from envDir, not inside it).
  const allowedRoot = path.resolve(envDir, '..');
  const allowedRootWithSep = allowedRoot.endsWith(path.sep) ? allowedRoot : allowedRoot + path.sep;
  for (const [label, p] of [['TLS_CERT_PATH', resolvedCert], ['TLS_KEY_PATH', resolvedKey]] as const) {
    if (!p.startsWith(allowedRootWithSep)) {
      throw new Error(`${label} '${p}' resolves outside the allowed directory (${allowedRoot})`);
    }
  }

  // S27: verify resolved paths are regular files, not devices or FIFOs
  for (const [label, p] of [['TLS_CERT_PATH', resolvedCert], ['TLS_KEY_PATH', resolvedKey]] as const) {
    if (!fs.existsSync(p) || !fs.statSync(p).isFile()) {
      throw new Error(`${label} '${p}' does not exist or is not a regular file`);
    }
  }

  const healthPort = parsePort(process.env.HEALTH_PORT, 'HEALTH_PORT');

  // Issue #18: optional rotating file log (in addition to stdout), cross-platform via pino-roll.
  const logFile = process.env.LOG_FILE
    ? path.resolve(envDir, process.env.LOG_FILE)
    : undefined;
  const logMaxFiles = parseInt(process.env.LOG_MAX_FILES ?? '5', 10);

  const metricsPort = parsePort(process.env.METRICS_PORT, 'METRICS_PORT');

  // Issue #12: optional WSS listener + static web viewer, off by default —
  // it's an extra TLS-terminated listener with its own attack surface, so it
  // stays opt-in same as HEALTH_PORT/METRICS_PORT.
  const wsPort = parsePort(process.env.WS_PORT, 'WS_PORT');

  return {
    listenPort:     port,
    sharedSecret:  secret,
    allowedSenders: new Set(senders),
    tlsCertPath:   resolvedCert,
    tlsKeyPath:    resolvedKey,
    metricsPort,
    wsPort,
    logLevel:      process.env.LOG_LEVEL ?? 'info',
    healthPort,
    logFile,
    logMaxSize:    process.env.LOG_MAX_SIZE ?? '10m',
    logMaxFiles:   isNaN(logMaxFiles) ? 5 : logMaxFiles,
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
