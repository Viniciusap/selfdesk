using ViewerWire = SelfDesk.Viewer.Protocol.WireProtocol;
using Xunit;

namespace SelfDesk.Viewer.Tests;

// Issue #25: fuzz WireProtocol.ParseHeader/DecodePeerId — the same shared parser
// broker/src/protocol.ts's TypeScript fuzz suite targets (tests/protocol-fuzz.test.ts).
// Mirrors sender/Sender.Tests/ProtocolFuzzTests.cs (both projects compile the same
// shared/dotnet/WireProtocol.cs). Seeded PRNG loops rather than a fuzzing framework
// (SharpFuzz/CsCheck need a native libFuzzer harness this repo doesn't otherwise
// build) — deterministic, no new dependency, and .NET spans are inherently
// bounds-checked so undersized input always fails with a normal, catchable
// exception, never memory corruption.
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
