using NLog;

namespace sim6502.Backend;

/// <summary>
/// REST transport to a real Ultimate 64.
///
/// machine:readmem and machine:writemem are DMA cycles on the cartridge bus
/// (route_machine.cc uses C64_DMA_RAW_WRITE), which is why writes to $DF1D reach
/// the UCI command FIFO exactly as a CPU write would.
///
/// Two firmware facts shape this class:
///   - PUT machine:writemem accepts at most 128 bytes of hex payload.
///   - Both endpoints address an ASCENDING SPAN. Writing N bytes at $DF1D would
///     land on $DF1E and beyond, so FIFO traffic must go one byte per request.
/// </summary>
public sealed class U64RestConnection : IU64Connection
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>Firmware limit for PUT machine:writemem.</summary>
    private const int MaxWriteChunk = 128;

    private readonly HttpClient _http;
    private readonly string _base;
    private readonly object _gate = new();
    private bool _disposed;

    public U64RestConnection(U64BackendConfig config)
        : this(config, new HttpClientHandler())
    {
    }

    internal U64RestConnection(U64BackendConfig config, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.Host))
            throw new ArgumentException(
                "The u64 backend needs a host. Set --u64-host.", nameof(config));

        _base = $"http://{config.Host}:{config.Port}/v1";
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(config.HttpTimeoutMs)
        };
    }

    public byte ReadByte(int address)
    {
        var body = ReadBytes(address, 1);
        if (body.Length < 1)
            throw new InvalidOperationException(
                $"Ultimate returned no data reading ${address:X4}");
        return body[0];
    }

    public byte[] ReadBytes(int address, int length)
    {
        lock (_gate)
        {
            var url = $"{_base}/machine:readmem?address={address:x}&length={length}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = Send(req, url);
            return resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        }
    }

    public void WriteByte(int address, byte value)
    {
        lock (_gate)
        {
            var url = $"{_base}/machine:writemem?address={address:x}&data={value:x2}";
            using var req = new HttpRequestMessage(HttpMethod.Put, url);
            using var resp = Send(req, url);
        }
    }

    public void WriteBytes(int address, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        for (var offset = 0; offset < data.Length; offset += MaxWriteChunk)
        {
            var take = Math.Min(MaxWriteChunk, data.Length - offset);
            var hex = Convert.ToHexString(data, offset, take).ToLowerInvariant();
            var target = address + offset;

            lock (_gate)
            {
                var url = $"{_base}/machine:writemem?address={target:x}&data={hex}";
                using var req = new HttpRequestMessage(HttpMethod.Put, url);
                using var resp = Send(req, url);
            }
        }
    }

    private HttpResponseMessage Send(HttpRequestMessage request, string url)
    {
        HttpResponseMessage response;
        try
        {
            response = _http.Send(request);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Ultimate request failed: {request.Method} {url}. {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new InvalidOperationException(
                $"Ultimate returned {(int)status} for {request.Method} {url}");
        }

        return response;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
