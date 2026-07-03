using Xunit;

namespace METERP.E2ETests;

/// <summary>
/// Serializes all E2E test classes — they share one docker app and demo tenant state.
/// </summary>
[CollectionDefinition("E2E", DisableParallelization = true)]
public sealed class E2ECollection;