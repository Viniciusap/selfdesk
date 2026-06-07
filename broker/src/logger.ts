import pino from 'pino';

export function createLogger(level = 'info'): pino.Logger {
  return pino({
    level,
    // S37: proactively redact common secret field names as defense-in-depth
    redact: {
      paths: ['secret', '*.secret', '*.password', '*.key', '*.token'],
      censor: '[REDACTED]',
    },
  });
}
