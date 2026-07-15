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
