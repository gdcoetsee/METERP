namespace METERP.Domain;

/// <summary>
/// Client / site sign-off gate before final invoicing (speed-to-cash control).
/// </summary>
public enum JobSignOffStatus
{
    None = 0,
    Pending = 1,
    SignedOff = 2
}