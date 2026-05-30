import pino from 'pino';

export function createLogger(level = 'info'): pino.Logger {
  return pino({ level });
}
