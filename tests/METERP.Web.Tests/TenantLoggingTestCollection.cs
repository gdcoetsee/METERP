using Xunit;

namespace METERP.Web.Tests;

/// <summary>
/// Serializes tests that swap Serilog's static <see cref="Serilog.Log.Logger"/>.
/// </summary>
[CollectionDefinition(nameof(TenantLoggingTestCollection), DisableParallelization = true)]
public class TenantLoggingTestCollection
{
}