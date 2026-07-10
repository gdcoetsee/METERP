namespace METERP.Web.Services;

public sealed class ConfirmRequest
{
    public required string Title { get; init; }
    public required string Message { get; init; }
    public string ConfirmLabel { get; init; } = "Confirm";
    public string CancelLabel { get; init; } = "Cancel";
    public string ConfirmVariant { get; init; } = "primary";
    public required TaskCompletionSource<bool> Completion { get; init; }
}

/// <summary>
/// Branded confirm dialogs — replaces window.confirm() across the app.
/// Only one host should be mounted (MainLayout or FieldLayout). Subscribe replaces prior handler
/// so a stray second host cannot stack double backdrops that block clicks.
/// </summary>
public sealed class ConfirmService
{
    private Action<ConfirmRequest>? _handler;

    /// <summary>Register the active dialog host (replaces any previous host).</summary>
    public void Subscribe(Action<ConfirmRequest> handler) => _handler = handler;

    public void Unsubscribe(Action<ConfirmRequest> handler)
    {
        if (_handler == handler)
            _handler = null;
    }

    public Task<bool> ConfirmAsync(
        string message,
        string title = "Confirm",
        string confirmLabel = "Confirm",
        string cancelLabel = "Cancel",
        string confirmVariant = "primary")
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new ConfirmRequest
        {
            Title = title,
            Message = message,
            ConfirmLabel = confirmLabel,
            CancelLabel = cancelLabel,
            ConfirmVariant = confirmVariant,
            Completion = tcs
        };

        if (_handler == null)
        {
            // No UI host — fail closed rather than hang forever.
            tcs.TrySetResult(false);
            return tcs.Task;
        }

        _handler.Invoke(request);
        return tcs.Task;
    }
}
