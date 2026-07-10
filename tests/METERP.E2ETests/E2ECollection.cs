using Xunit;

namespace METERP.E2ETests;

/// <summary>
/// Serial E2E collection with a shared browser fixture.
/// </summary>
[CollectionDefinition("E2E", DisableParallelization = true)]
public sealed class E2ECollection : ICollectionFixture<E2EBrowserFixture>;
