using System.Net;
using System.Net.Sockets;
using System.Text;

namespace METERP.Application.Tests.Support;

/// <summary>
/// Minimal loopback SMTP server for integration-testing SmtpEmailSender without external dependencies.
/// </summary>
public sealed class LoopbackSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptTask;
    private readonly SemaphoreSlim _messageReceived = new(0);

    public int Port { get; }
    public List<string> ReceivedMessages { get; } = new();

    public LoopbackSmtpServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptTask = Task.Run(AcceptLoopAsync);
    }

    public async Task WaitForMessageAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        linked.CancelAfter(timeout);
        await _messageReceived.WaitAsync(linked.Token);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => HandleClientAsync(client), _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using var _ = client;
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" };

        await writer.WriteLineAsync("220 localhost ESMTP METERP-Test");

        var inData = false;
        var data = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line == null)
                break;

            if (inData)
            {
                if (line == ".")
                {
                    ReceivedMessages.Add(data.ToString());
                    _messageReceived.Release();
                    await writer.WriteLineAsync("250 OK");
                    inData = false;
                    continue;
                }

                data.AppendLine(line);
                continue;
            }

            var cmd = line.Split(' ', 2)[0].ToUpperInvariant();
            switch (cmd)
            {
                case "EHLO":
                case "HELO":
                    await writer.WriteLineAsync("250-localhost Hello");
                    await writer.WriteLineAsync("250 OK");
                    break;
                case "MAIL":
                case "RCPT":
                    await writer.WriteLineAsync("250 OK");
                    break;
                case "DATA":
                    await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                    inData = true;
                    break;
                case "QUIT":
                    await writer.WriteLineAsync("221 Bye");
                    return;
                default:
                    await writer.WriteLineAsync("250 OK");
                    break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        try { await _acceptTask; } catch (OperationCanceledException) { }
        _cts.Dispose();
        _messageReceived.Dispose();
    }
}