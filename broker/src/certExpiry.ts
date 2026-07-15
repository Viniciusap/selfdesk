import { X509Certificate } from 'node:crypto';
import type { Logger } from 'pino';

const WARN_WITHIN_DAYS = 30;

// Issue #17 (broker-side note): warn at startup before the TLS server cert expires.
export function warnIfExpiringSoon(certPem: Buffer, log: Logger): void {
  let cert: X509Certificate;
  try {
    cert = new X509Certificate(certPem);
  } catch {
    return;
  }

  const daysLeft = (new Date(cert.validTo).getTime() - Date.now()) / 86_400_000;

  if (daysLeft < 0) {
    log.warn({ validTo: cert.validTo }, 'TLS server certificate has expired — regenerate with bootstrap');
  } else if (daysLeft < WARN_WITHIN_DAYS) {
    log.warn({ daysLeft: Math.floor(daysLeft), validTo: cert.validTo },
      'TLS server certificate expiring soon — regenerate with bootstrap');
  }
}
