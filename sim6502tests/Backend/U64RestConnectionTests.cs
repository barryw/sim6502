using System.Net;
using FluentAssertions;
using sim6502.Backend;
using Xunit;

namespace sim6502tests.Backend;

public class U64RestConnectionTests
{
    /// <summary>Records every request and replies with canned bytes.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public readonly List<string> Urls = new();
        public byte[] Body = { 0x00 };

        protected override HttpResponseMessage Send(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // OriginalString (not ToString()/AbsoluteUri) because Uri normalizes
            // away the default HTTP port (80) when rendering — that's correct
            // HTTP behavior, but it would hide the port from these assertions.
            lock (Urls) Urls.Add($"{request.Method} {request.RequestUri!.OriginalString}");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Body)
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Send(request, cancellationToken));
    }

    private static U64BackendConfig Config() =>
        new() { Host = "10.0.0.5", Port = 80 };

    [Fact]
    public void ReadByte_RequestsLengthOneAtHexAddress()
    {
        // length=1 is load-bearing: a span covering $DF1E/$DF1F POPS those FIFOs
        // on real hardware and silently eats the reply.
        var handler = new RecordingHandler { Body = new byte[] { 0xC9 } };
        using var conn = new U64RestConnection(Config(), handler);

        conn.ReadByte(0xDF1D).Should().Be(0xC9);
        handler.Urls.Should().ContainSingle()
            .Which.Should().Be("GET http://10.0.0.5:80/v1/machine:readmem?address=df1d&length=1");
    }

    [Fact]
    public void WriteByte_PutsTwoDigitHexData()
    {
        var handler = new RecordingHandler();
        using var conn = new U64RestConnection(Config(), handler);

        conn.WriteByte(0xDF1C, 0x01);
        handler.Urls.Should().ContainSingle()
            .Which.Should().Be("PUT http://10.0.0.5:80/v1/machine:writemem?address=df1c&data=01");
    }

    [Fact]
    public void WriteBytes_ChunksToTheFirmwareLimitOf128()
    {
        // PUT machine:writemem rejects more than 128 bytes
        // (route_machine.cc: "Maximum length of 128 bytes exceeded").
        var handler = new RecordingHandler();
        using var conn = new U64RestConnection(Config(), handler);

        conn.WriteBytes(0x1000, new byte[300]);

        handler.Urls.Should().HaveCount(3);
        handler.Urls[0].Should().Contain("address=1000");
        handler.Urls[1].Should().Contain("address=1080");
        handler.Urls[2].Should().Contain("address=1100");
    }

    [Fact]
    public void Requests_AreSerialized()
    {
        // Concurrent requests can lock the machine up, so the connection must
        // serialize internally rather than trusting callers.
        var handler = new SlowHandler();
        using var conn = new U64RestConnection(Config(), handler);

        Parallel.For(0, 8, _ => conn.ReadByte(0xDF1C));

        handler.MaxConcurrent.Should().Be(1);
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        private int _current;
        public int MaxConcurrent;

        // U64RestConnection uses the synchronous HttpClient.Send API (the whole
        // IExecutionBackend surface is synchronous), so the fake must implement
        // the synchronous Send override — the base HttpMessageHandler.Send
        // throws NotSupportedException unless a subclass provides it.
        protected override HttpResponseMessage Send(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var now = Interlocked.Increment(ref _current);
            InterlockedMax(ref MaxConcurrent, now);
            Thread.Sleep(20);
            Interlocked.Decrement(ref _current);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 0 })
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Send(request, cancellationToken));

        private static void InterlockedMax(ref int target, int value)
        {
            int seen;
            do
            {
                seen = Volatile.Read(ref target);
                if (value <= seen) return;
            } while (Interlocked.CompareExchange(ref target, value, seen) != seen);
        }
    }
}
