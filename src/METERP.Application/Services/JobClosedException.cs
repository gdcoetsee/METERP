namespace METERP.Application.Services;

/// <summary>
/// Thrown when a mutation is attempted on a job that has been executive-closed.
/// </summary>
public sealed class JobClosedException : InvalidOperationException
{
    public JobClosedException(string message) : base(message) { }

    public static JobClosedException ForJob(string jobNumber) =>
        new($"Job {jobNumber} is closed. Reopen with an executive reason to capture further costs.");
}