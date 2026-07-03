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
/// </summary>
public sealed class ConfirmService
{
    public event Action<ConfirmRequest>? OnShow;

    public Task<bool> ConfirmAsync(
        string message,
        string title = "Confirm",
        string confirmLabel = "Confirm",
        string cancelLabel = "Cancel",
        string confirmVariant = "primary")
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        OnShow?.Invoke(new ConfirmRequest
        {
            Title = title,
            Message = message,
            ConfirmLabel = confirmLabel,
            CancelLabel = cancelLabel,
            ConfirmVariant = confirmVariant,
            Completion = tcs
        });
        return tcs.Task;
    }
}