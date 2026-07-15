import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { execFileSync } from 'node:child_process';
import { createLogger } from '../src/logger';

describe('createLogger', () => {
  it('logs to stdout only when no logFile is given', () => {
    const log = createLogger('info');
    expect(() => log.info('hello')).not.toThrow();
  });

  it('accepts a rotating file target without throwing', async () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'selfdesk-log-'));
    const file = path.join(dir, 'broker.log');
    const log = createLogger('info', file, '10m', 5);
    expect(() => log.info('hello file')).not.toThrow();
    // pino-roll's transport runs on a worker thread — give it a moment before
    // removing the directory out from under it.
    await new Promise((resolve) => setTimeout(resolve, 300));
    fs.rmSync(dir, { recursive: true, force: true });
  });

  it('still writes to stdout when a logFile is configured (issue #18 regression)', () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'selfdesk-log-'));
    const file = path.join(dir, 'broker.log');
    // pino's file/worker transports write to real fds, invisible to a jest-internal
    // spy on process.stdout.write — run it in a child process and capture its fd 1.
    const script = `
      const { createLogger } = require('${path.join(__dirname, '../src/logger').replace(/\\/g, '\\\\')}');
      const log = createLogger('info', '${file.replace(/\\/g, '\\\\')}', '10m', 5);
      log.info('hello both');
      setTimeout(() => {}, 300);
    `;
    const stdout = execFileSync(
      process.execPath,
      ['-r', 'ts-node/register', '-e', script],
      { encoding: 'utf8', timeout: 10_000 },
    );
    expect(stdout).toContain('hello both');
    fs.rmSync(dir, { recursive: true, force: true });
  });
});
