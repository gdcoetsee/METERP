namespace METERP.Domain;

/// <summary>
/// Internal work acceptance chain before final/partial invoicing.
/// Separate from executive job close (billing vs file lock).
/// </summary>
public enum JobSignOffStatus
{
    None = 0,

    /// <summary>Awaiting divisional manager work acceptance.</summary>
    PendingManager = 1,

    /// <summary>Legacy alias for <see cref="PendingManager"/>.</summary>
    Pending = 1,

    /// <summary>Fully signed off (manager + executive).</summary>
    SignedOff = 2,

    /// <summary>Manager accepted; awaiting executive work sign-off.</summary>
    PendingExecutive = 3
}
