using System.Timers;

namespace METERP.Web.Services;

/// <summary>
/// Simple toast notification service for user feedback.
/// </summary>
public class ToastService : IDisposable
{
    public event Action<ToastMessage>? OnShow;

    private readonly System.Timers.Timer _timer = new();

    public ToastService()
    {
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = false;
    }

    public void ShowSuccess(string message, int durationMs = 4000)
    {
        Show(new ToastMessage(message, ToastType.Success, durationMs));
    }

    public void ShowError(string message, int durationMs = 6000)
    {
        Show(new ToastMessage(message, ToastType.Error, durationMs));
    }

    public void ShowInfo(string message, int durationMs = 4000)
    {
        Show(new ToastMessage(message, ToastType.Info, durationMs));
    }

    private void Show(ToastMessage toast)
    {
        OnShow?.Invoke(toast);

        if (toast.DurationMs > 0)
        {
            _timer.Interval = toast.DurationMs;
            _timer.Start();
        }
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        // Auto hide is handled by the component via duration, but we can extend if needed.
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}

public record ToastMessage(string Message, ToastType Type, int DurationMs);

public enum ToastType
{
    Success,
    Error,
    Info
}