using System.Net;
using System.Net.Sockets;
using System.Text;

namespace METERP.Web.Tests.Support;

/// <summary>
/// Minimal loopback OTLP/HTTP receiver for integration-testing trace and metric export.
/// </summary>
public sealed class LoopbackOtlpCollector : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _listenTask;
    private readonly SemaphoreSlim _traceReceived = new(0);
    private readonly SemaphoreSlim _metricReceived = new(0);

    public int Port { get; }
    public string Endpoint => $"http://127.0.0.1:{Port}";
    public int TraceExportCount { get; private set; }
    public int MetricExportCount { get; private set; }
    public List<byte[]> TracePayloads { get; } = new();

    public LoopbackOtlpCollector()
    {
        Port = GetFreeTcpPort();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _listenTask = Task.Run(ListenLoopAsync);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async Task WaitForTraceExportAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        linked.CancelAfter(timeout);
        await _traceReceived.WaitAsync(linked.Token);
    }

    public async Task WaitForMetricExportAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        linked.CancelAfter(timeout);
        await _metricReceived.WaitAsync(linked.Token);
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException) when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ = Task.Run(() => HandleRequestAsync(context), _cts.Token);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? string.Empty;
            if (context.Request.HttpMethod == "POST" &&
                (path.Equals("/v1/traces", StringComparison.OrdinalIgnoreCase) ||
                 path.Equals("/v1/metrics", StringComparison.OrdinalIgnoreCase)))
            {
                using var ms = new MemoryStream();
                await context.Request.InputStream.CopyToAsync(ms);
                var payload = ms.ToArray();

                if (path.Equals("/v1/traces", StringComparison.OrdinalIgnoreCase))
                {
                    TraceExportCount++;
                    TracePayloads.Add(payload);
                    _traceReceived.Release();
                }
                else
                {
                    MetricExportCount++;
                    _metricReceived.Release();
                }

                context.Response.StatusCode = (int)HttpStatusCode.OK;
                var body = Encoding.UTF8.GetBytes("{\"partialSuccess\":{}}");
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(body);
            }
            else
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            }
        }
        finally
        {
            context.Response.Close();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        _listener.Close();
        try { await _listenTask; } catch (OperationCanceledException) { }
        _cts.Dispose();
        _traceReceived.Dispose();
        _metricReceived.Dispose();
    }
}