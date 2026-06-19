namespace METERP.Web.Services;

public interface IE2EEmailCaptureStore
{
    bool IsCapturing { get; }

    void BeginCapture();

    void Clear();

    IReadOnlyList<CapturedEmailMessage> GetAll();

    void Record(string to, string subject, string htmlBody);
}

public sealed record CapturedEmailMessage(string To, string Subject, string HtmlBody, DateTimeOffset CapturedAtUtc);

public sealed class E2EEmailCaptureStore : IE2EEmailCaptureStore
{
    private readonly object _sync = new();
    private readonly List<CapturedEmailMessage> _messages = new();
    private bool _capturing;

    public bool IsCapturing
    {
        get { lock (_sync) return _capturing; }
    }

    public void BeginCapture()
    {
        lock (_sync)
        {
            _capturing = true;
            _messages.Clear();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _capturing = false;
            _messages.Clear();
        }
    }

    public IReadOnlyList<CapturedEmailMessage> GetAll()
    {
        lock (_sync)
            return _messages.ToList();
    }

    public void Record(string to, string subject, string htmlBody)
    {
        lock (_sync)
        {
            if (!_capturing)
                return;

            _messages.Add(new CapturedEmailMessage(to, subject, htmlBody, DateTimeOffset.UtcNow));
        }
    }
}