import * as selfsigned from 'selfsigned';
import type { Logger } from 'pino';
import { warnIfExpiringSoon } from '../src/certExpiry';

// Minimal logger stub capturing warn() calls; certExpiry only uses log.warn.
function makeLog() {
  const warnings: { obj: unknown; msg: string }[] = [];
  const log = {
    warn: (obj: unknown, msg?: string) => { warnings.push({ obj, msg: msg ?? '' }); },
  };
  return { log: log as unknown as Logger, warnings };
}

function certValidFor(days: number): Buffer {
  const pems = selfsigned.generate([{ name: 'commonName', value: '127.0.0.1' }], { days });
  return Buffer.from(pems.cert);
}

describe('warnIfExpiringSoon', () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('does not warn for a certificate valid well beyond the threshold', () => {
    const { log, warnings } = makeLog();
    warnIfExpiringSoon(certValidFor(365), log);
    expect(warnings).toHaveLength(0);
  });

  it('warns that the certificate is expiring soon when inside the 30-day window', () => {
    const { log, warnings } = makeLog();
    warnIfExpiringSoon(certValidFor(10), log);
    expect(warnings).toHaveLength(1);
    expect(warnings[0].msg).toContain('expiring soon');
    expect((warnings[0].obj as { daysLeft: number }).daysLeft).toBeLessThan(30);
  });

  it('warns that the certificate has expired when validTo is in the past', () => {
    const { log, warnings } = makeLog();
    const cert = certValidFor(365);
    // Jump 400 days into the future so the 365-day cert reads as expired.
    jest.spyOn(Date, 'now').mockReturnValue(Date.now() + 400 * 86_400_000);
    warnIfExpiringSoon(cert, log);
    expect(warnings).toHaveLength(1);
    expect(warnings[0].msg).toContain('has expired');
  });

  it('silently ignores an unparseable certificate buffer', () => {
    const { log, warnings } = makeLog();
    expect(() => warnIfExpiringSoon(Buffer.from('not a certificate'), log)).not.toThrow();
    expect(warnings).toHaveLength(0);
  });
});
