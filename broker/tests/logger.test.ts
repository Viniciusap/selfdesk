import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
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
});
