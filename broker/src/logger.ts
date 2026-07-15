import pino from 'pino';

// Issue #18: optional rotating file log via pino-roll, in addition to stdout.
// Cross-platform (unlike logrotate), so it works for both install-broker.sh and
// install-broker.ps1 deployments.
export function createLogger(
  level = 'info',
  logFile?: string,
  logMaxSize = '10m',
  logMaxFiles = 5,
): pino.Logger {
  const opts: pino.LoggerOptions = {
    level,
    // S37: proactively redact common secret field names as defense-in-depth
    redact: {
      paths: ['secret', '*.secret', '*.password', '*.key', '*.token'],
      censor: '[REDACTED]',
    },
  };

  if (!logFile) return pino(opts);

  const transport = pino.transport({
    targets: [
      {
        target: 'pino-roll',
        options: { file: logFile, size: logMaxSize, mkdir: true, limit: { count: logMaxFiles } },
        level,
      },
      { target: 'pino/file', options: { destination: 1 }, level },
    ],
  });

  return pino(opts, transport);
}
