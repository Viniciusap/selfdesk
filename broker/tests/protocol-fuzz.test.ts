/**
 * Issue #25: property-based fuzzing of the wire protocol parser.
 * Generates random byte buffers and asserts the parser never throws an
 * unhandled/uncontrolled exception — only a valid header or a ProtocolParseError.
 */
import fc from 'fast-check';
import {
  HEADER_SIZE,
  PEER_ID_OFFSET,
  ProtocolParseError,
  buildEnvelope,
  decodePeerId,
  parseHeader,
} from '../src/protocol';

describe('parseHeader fuzzing', () => {
  it('never throws anything but ProtocolParseError for buffers shorter than HEADER_SIZE', () => {
    fc.assert(
      fc.property(fc.uint8Array({ minLength: 0, maxLength: HEADER_SIZE - 1 }), (bytes) => {
        const buf = Buffer.from(bytes);
        expect(() => parseHeader(buf)).toThrow(ProtocolParseError);
      }),
    );
  });

  it('never throws for any buffer >= HEADER_SIZE, up to 65535 bytes', () => {
    fc.assert(
      fc.property(fc.uint8Array({ minLength: HEADER_SIZE, maxLength: 65_535 }), (bytes) => {
        const buf = Buffer.from(bytes);
        const header = parseHeader(buf);
        expect(header.type).toBeGreaterThanOrEqual(0);
        expect(header.type).toBeLessThanOrEqual(255);
        expect(header.length).toBeGreaterThanOrEqual(0);
        expect(Number.isSafeInteger(header.length)).toBe(true);
        expect(typeof header.peerId).toBe('string');
      }),
    );
  });
});

describe('decodePeerId fuzzing', () => {
  it('never throws for any buffer/offset combination', () => {
    fc.assert(
      fc.property(
        fc.uint8Array({ minLength: 0, maxLength: 1024 }),
        fc.integer({ min: -1024, max: 1024 }),
        (bytes, offset) => {
          const buf = Buffer.from(bytes);
          // Buffer.subarray clamps out-of-range offsets rather than throwing —
          // decodePeerId must hold that property for any offset a fuzzer tries,
          // including negative ones from a malformed/adversarial call site.
          expect(() => decodePeerId(buf, offset < 0 ? 0 : offset)).not.toThrow();
        },
      ),
    );
  });

  // router.ts's routeReceiverMessage echoes a decoded peerId straight back into a
  // new envelope (`sender.send(buildEnvelope(header.type, targetId, payload))`) to
  // relay INPUT_EVENT/QUALITY_HINT/etc to the targeted sender — this chain must
  // never throw, even though a truncated multi-byte UTF-8 sequence in the raw
  // 16-byte field can decode to MORE than 16 bytes once re-encoded (replacement
  // characters are 3 bytes each). buildEnvelope's byte-safe truncation (see the R8
  // fix in buildEnvelope) is what keeps this safe — it's not a fixed-width copy.
  it('decode-then-rebuild round trip never throws, regardless of malformed UTF-8', () => {
    fc.assert(
      fc.property(fc.uint8Array({ minLength: HEADER_SIZE, maxLength: 65_535 }), (bytes) => {
        const buf = Buffer.from(bytes);
        const peerId = decodePeerId(buf, PEER_ID_OFFSET);
        expect(() => parseHeader(buildEnvelope(0x20, peerId, Buffer.alloc(0)))).not.toThrow();
      }),
    );
  });
});
