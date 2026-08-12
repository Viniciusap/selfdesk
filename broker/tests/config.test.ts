import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { loadConfig } from '../src/config';

function writeEnv(dir: string, extra: Record<string, string> = {}): string {
  const certPath = path.join(dir, 'server-cert.pem');
  const keyPath  = path.join(dir, 'server-key.pem');
  fs.writeFileSync(certPath, 'cert');
  fs.writeFileSync(keyPath, 'key');

  const lines = [
    'SHARED_SECRET=' + 'a'.repeat(32),
    'TLS_CERT_PATH=./server-cert.pem',
    'TLS_KEY_PATH=./server-key.pem',
    ...Object.entries(extra).map(([k, v]) => `${k}=${v}`),
  ];
  const envFile = path.join(dir, '.env');
  fs.writeFileSync(envFile, lines.join('\n'));
  return envFile;
}

describe('loadConfig — M6 TLS path containment', () => {
  let tmpRoot: string;

  beforeEach(() => {
    tmpRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'selfdesk-cfg-'));
    // dotenv's config() does not override already-set process.env vars, so a
    // previous test's values would otherwise leak into the next one.
    delete process.env.SHARED_SECRET;
    delete process.env.TLS_CERT_PATH;
    delete process.env.TLS_KEY_PATH;
  });

  afterEach(() => {
    fs.rmSync(tmpRoot, { recursive: true, force: true });
  });

  it('allows the standard ../certs/ convention (sibling of the .env directory)', () => {
    const brokerDir = path.join(tmpRoot, 'broker');
    const certsDir  = path.join(tmpRoot, 'certs');
    fs.mkdirSync(brokerDir);
    fs.mkdirSync(certsDir);
    fs.writeFileSync(path.join(certsDir, 'server-cert.pem'), 'cert');
    fs.writeFileSync(path.join(certsDir, 'server-key.pem'), 'key');

    const envFile = path.join(brokerDir, '.env');
    fs.writeFileSync(envFile, [
      'SHARED_SECRET=' + 'a'.repeat(32),
      'TLS_CERT_PATH=../certs/server-cert.pem',
      'TLS_KEY_PATH=../certs/server-key.pem',
    ].join('\n'));

    expect(() => loadConfig(envFile)).not.toThrow();
  });

  it('allows certs inside the .env directory itself', () => {
    const envFile = writeEnv(tmpRoot);
    expect(() => loadConfig(envFile)).not.toThrow();
  });

  it('rejects a deep traversal escape outside the repo root', () => {
    const brokerDir = path.join(tmpRoot, 'broker');
    fs.mkdirSync(brokerDir);
    const envFile = path.join(brokerDir, '.env');
    fs.writeFileSync(envFile, [
      'SHARED_SECRET=' + 'a'.repeat(32),
      'TLS_CERT_PATH=../../../../../../etc/passwd',
      'TLS_KEY_PATH=../../../../../../etc/passwd',
    ].join('\n'));

    expect(() => loadConfig(envFile)).toThrow(/resolves outside the allowed directory/);
  });
});

describe('loadConfig — validation errors', () => {
  let tmpRoot: string;

  beforeEach(() => {
    tmpRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'selfdesk-cfgv-'));
    // dotenv does not override already-set process.env, so clear every var
    // loadConfig reads to keep these cases independent of each other.
    for (const k of ['SHARED_SECRET', 'TLS_CERT_PATH', 'TLS_KEY_PATH',
      'LISTEN_PORT', 'HEALTH_PORT', 'METRICS_PORT', 'WS_PORT']) {
      delete process.env[k];
    }
  });

  afterEach(() => {
    fs.rmSync(tmpRoot, { recursive: true, force: true });
  });

  function writeLines(lines: string[]): string {
    const envFile = path.join(tmpRoot, '.env');
    fs.writeFileSync(envFile, lines.join('\n'));
    return envFile;
  }

  function validCerts(): string[] {
    fs.writeFileSync(path.join(tmpRoot, 'server-cert.pem'), 'cert');
    fs.writeFileSync(path.join(tmpRoot, 'server-key.pem'), 'key');
    return ['TLS_CERT_PATH=./server-cert.pem', 'TLS_KEY_PATH=./server-key.pem'];
  }

  it('throws on a non-numeric LISTEN_PORT', () => {
    const envFile = writeLines([
      'LISTEN_PORT=not-a-port',
      'SHARED_SECRET=' + 'a'.repeat(32),
      ...validCerts(),
    ]);
    expect(() => loadConfig(envFile)).toThrow(/Invalid LISTEN_PORT/);
  });

  it('throws on an out-of-range LISTEN_PORT', () => {
    const envFile = writeLines([
      'LISTEN_PORT=99999',
      'SHARED_SECRET=' + 'a'.repeat(32),
      ...validCerts(),
    ]);
    expect(() => loadConfig(envFile)).toThrow(/Invalid LISTEN_PORT/);
  });

  it('throws when SHARED_SECRET is missing', () => {
    const envFile = writeLines([...validCerts()]);
    expect(() => loadConfig(envFile)).toThrow(/SHARED_SECRET missing or too short/);
  });

  it('throws when SHARED_SECRET is shorter than 32 characters', () => {
    const envFile = writeLines(['SHARED_SECRET=' + 'a'.repeat(31), ...validCerts()]);
    expect(() => loadConfig(envFile)).toThrow(/SHARED_SECRET missing or too short/);
  });

  it('throws when TLS paths are missing', () => {
    const envFile = writeLines(['SHARED_SECRET=' + 'a'.repeat(32)]);
    expect(() => loadConfig(envFile)).toThrow(/TLS_CERT_PATH and TLS_KEY_PATH are required/);
  });

  it('throws when the TLS certificate file does not exist', () => {
    const envFile = writeLines([
      'SHARED_SECRET=' + 'a'.repeat(32),
      'TLS_CERT_PATH=./missing-cert.pem',
      'TLS_KEY_PATH=./missing-key.pem',
    ]);
    expect(() => loadConfig(envFile)).toThrow(/does not exist or is not a regular file/);
  });
});
