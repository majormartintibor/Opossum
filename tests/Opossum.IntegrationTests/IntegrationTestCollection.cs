namespace Opossum.IntegrationTests;

/// <summary>
/// Defines a test collection for integration tests that should not run in parallel.
/// This ensures proper test isolation when tests share resources like file system storage.
/// 
/// xUnit best practice: Use test collections to group tests that:
/// - Share expensive setup/teardown
/// - Access shared resources (files, databases, etc.)
/// - Should not run concurrently due to race conditions
/// </summary>
[CollectionDefinition(nameof(IntegrationTestCollection), DisableParallelization = true)]
public class IntegrationTestCollection : ICollectionFixture<OpossumFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
