using SenderWire = SelfDesk.Sender.Protocol.WireProtocol;
using Xunit;

namespace SelfDesk.Sender.Tests;

// Issue #25: fuzz WireProtocol.ParseHeader/DecodePeerId — the same shared parser
// broker/src/protocol.ts's TypeScript fuzz suite targets (tests/protocol-fuzz.test.ts).
// Seeded PRNG loops rather than a fuzzing framework (SharpFuzz/CsCheck need a native
// libFuzzer harness this repo doesn't otherwise build) — deterministic, no new
// dependency, and .NET spans are inherently bounds-checked so the failure mode for
// undersized input is always a normal, catchable exception, never memory corruption.
public class ProtocolFuzzTests
{
    private const int Iterations = 2000;

    [Fact]
    public void ParseHeader_NeverThrowsForAnyFullSizeHeader()
    {
        var rng = new Random(12345);
        var header = new byte[ProtocolSizes.HeaderSize];
        for (var i = 0; i < Iterations; i++)
        {
            rng.NextBytes(header);
            var (type, peerId, length) = WireProtocol.ParseHeader(header);
            Assert.InRange(type, byte.MinValue, byte.MaxValue);
            Assert.NotNull(peerId);
            Assert.InRange(length, 0u, uint.MaxValue);
        }
    }

    [Fact]
    public void ParseHeader_UndersizedSpan_ThrowsBoundsException_NeverSilentlyWrong()
    {
        // Spans are inherently bounds-checked in .NET, so an undersized header can
        // only ever fail with a well-defined bounds exception (never silent wrong
        // data or memory corruption) — the exact type depends on which internal
        // access fails first (direct indexer vs. Slice vs. BinaryPrimitives).
        for (var len = 0; len < ProtocolSizes.HeaderSize; len++)
        {
            var span = new byte[len];
            Exception? caught = null;
            try { WireProtocol.ParseHeader(span); }
            catch (Exception ex) { caught = ex; }

            Assert.True(
                caught is IndexOutOfRangeException or ArgumentOutOfRangeException,
                $"len={len}: expected a bounds exception, got {caught?.GetType().FullName ?? "no exception"}");
        }
    }

    [Fact]
    public void DecodePeerId_NeverThrowsForAnyFullSizeHeader()
    {
        var rng = new Random(6789);
        var header = new byte[ProtocolSizes.HeaderSize];
        for (var i = 0; i < Iterations; i++)
        {
            rng.NextBytes(header);
            var peerId = WireProtocol.DecodePeerId(header);
            Assert.NotNull(peerId);
        }
    }

    [Fact]
    public void BuildEnvelope_RandomPeerIdAndPayload_RoundTripsWithoutThrowing()
    {
        var rng = new Random(999);
        for (var i = 0; i < Iterations; i++)
        {
            var peerIdBytes = new byte[rng.Next(0, 64)];
            rng.NextBytes(peerIdBytes);
            // Intentionally allow invalid UTF-8 byte sequences — GetString substitutes
            // replacement characters rather than throwing, and BuildEnvelope's peerId
            // truncation must still hold for whatever string comes out of that.
            var peerId = System.Text.Encoding.UTF8.GetString(peerIdBytes);

            var payload = new byte[rng.Next(0, 256)];
            rng.NextBytes(payload);

            var envelope = WireProtocol.BuildEnvelope(MessageType.Hello, peerId, payload);
            var (type, _, length) = WireProtocol.ParseHeader(envelope);
            Assert.Equal(MessageType.Hello, type);
            Assert.Equal((uint)payload.Length, length);
        }
    }
}
