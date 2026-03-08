using Opossum.Core;
using Opossum.DependencyInjection;
using Opossum.Extensions;
using Opossum.Projections;

namespace Opossum.IntegrationTests.Projections;

/// <summary>
/// Integration tests verifying that post-rebuild reads work correctly without
/// an aggregated <c>Metadata/index.json</c> file.  After a rebuild, the metadata
/// index is not written — all projection state is served from per-file embedded
/// metadata wrappers.  These tests confirm the lazy metadata index correctly
/// handles the missing file and that the cache is not stale.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class PostRebuildMetadataDecouplingTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testStoragePath;
    private readonly string _projectionPath;
    private readonly IEventStore _eventStore;
    private readonly IProjectionManager _projectionManager;
    private readonly IProjectionRebuilder _projectionRebuilder;

    public PostRebuildMetadataDecouplingTests()
    {
        _testStoragePath = Path.Combine(
            Path.GetTempPath(),
            "OpossumPostRebuildMetadataTests",
            Guid.NewGuid().ToString());

        var services = new ServiceCollection();

        services.AddOpossum(options =>
        {
            options.RootPath = _testStoragePath;
            options.UseStore("MetadataContext");
        });

        services.AddProjections(options =>
        {
            options.ScanAssembly(typeof(PostRebuildMetadataDecouplingTests).Assembly);
        });

        _serviceProvider = services.BuildServiceProvider();
        _eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        _projectionManager = _serviceProvider.GetRequiredService<IProjectionManager>();
        _projectionRebuilder = _serviceProvider.GetRequiredService<IProjectionRebuilder>();

        _projectionPath = Path.Combine(
            _testStoragePath, "MetadataContext", "Projections", "AccountBalance");
    }

    [Fact]
    public async Task PostRebuild_MetadataIndexFile_DoesNotExistAsync()
    {
        // Arrange
        var projection = new AccountBalanceProjection();
        _projectionManager.RegisterProjection(projection);

        var accountId = Guid.NewGuid();
        var events = new[]
        {
            new AccountCreatedEvent(accountId, "Alice", 500m)
                .ToDomainEvent()
                .WithTag("accountId", accountId.ToString())
                .Build(),
            new MoneyDepositedEvent(accountId, 200m)
                .ToDomainEvent()
                .WithTag("accountId", accountId.ToString())
                .Build()
        };

        await _eventStore.AppendAsync(events, null);

        // Act
        await _projectionRebuilder.RebuildAsync("AccountBalance");

        // Assert — aggregated metadata index must NOT exist after rebuild
        var metadataIndexFile = Path.Combine(_projectionPath, "Metadata", "index.json");
        Assert.False(File.Exists(metadataIndexFile),
            "Metadata/index.json should not exist after rebuild — metadata is embedded per-file.");
    }

    [Fact]
    public async Task PostRebuild_GetAsync_ReturnsCorrectStateAsync()
    {
        // Arrange
        var projection = new AccountBalanceProjection();
        _projectionManager.RegisterProjection(projection);

        var accountId = Guid.NewGuid();
        var events = new[]
        {
            new AccountCreatedEvent(accountId, "Bob", 1000m)
                .ToDomainEvent()
                .WithTag("accountId", accountId.ToString())
                .Build(),
            new MoneyDepositedEvent(accountId, 500m)
                .ToDomainEvent()
                .WithTag("accountId", accountId.ToString())
                .Build(),
            new MoneyWithdrawnEvent(accountId, 200m)
                .ToDomainEvent()
                .WithTag("accountId", accountId.ToString())
                .Build()
        };

        await _eventStore.AppendAsync(events, null);
        await _projectionRebuilder.RebuildAsync("AccountBalance");

        // Act — read via the public store interface (no aggregated index on disk)
        var store = _serviceProvider.GetRequiredService<IProjectionStore<AccountBalanceState>>();
        var state = await store.GetAsync(accountId.ToString());

        // Assert
        Assert.NotNull(state);
        Assert.Equal(accountId, state.AccountId);
        Assert.Equal("Bob", state.OwnerName);
        Assert.Equal(1300m, state.Balance); // 1000 + 500 - 200
        Assert.Equal(3, state.TransactionCount);
    }

    [Fact]
    public async Task PostRebuild_GetAllAsync_ReturnsAllProjectionsAsync()
    {
        // Arrange
        var projection = new AccountBalanceProjection();
        _projectionManager.RegisterProjection(projection);

        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var events = ids
            .Select((id, i) => new AccountCreatedEvent(id, $"User{i}", 100m * (i + 1))
                .ToDomainEvent()
                .WithTag("accountId", id.ToString())
                .Build())
            .ToArray();

        await _eventStore.AppendAsync(events, null);
        await _projectionRebuilder.RebuildAsync("AccountBalance");

        // Act
        var store = _serviceProvider.GetRequiredService<IProjectionStore<AccountBalanceState>>();
        var all = await store.GetAllAsync();

        // Assert
        Assert.Equal(5, all.Count);
        foreach (var id in ids)
        {
            Assert.Contains(all, s => s.AccountId == id);
        }
    }

    [Fact]
    public async Task PostRebuild_QueryAsync_FiltersCorrectlyAsync()
    {
        // Arrange
        var projection = new AccountBalanceProjection();
        _projectionManager.RegisterProjection(projection);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var events = new[]
        {
            new AccountCreatedEvent(id1, "Charlie", 100m)
                .ToDomainEvent()
                .WithTag("accountId", id1.ToString())
                .Build(),
            new AccountCreatedEvent(id2, "Diana", 200m)
                .ToDomainEvent()
                .WithTag("accountId", id2.ToString())
                .Build()
        };

        await _eventStore.AppendAsync(events, null);
        await _projectionRebuilder.RebuildAsync("AccountBalance");

        // Act — predicate query exercises GetAllAsync under the hood (no metadata index needed)
        var store = _serviceProvider.GetRequiredService<IProjectionStore<AccountBalanceState>>();
        var results = await store.QueryAsync(s => s.Balance >= 200m);

        // Assert
        Assert.Single(results);
        Assert.Equal("Diana", results[0].OwnerName);
    }

    [Fact]
    public async Task PostRebuild_NormalSave_StartsMetadataVersionFromOneAsync()
    {
        // Arrange — seed events and rebuild so the projection exists on disk
        var projection = new AccountBalanceProjection();
        _projectionManager.RegisterProjection(projection);

        var accountId = Guid.NewGuid();
        var events = new[]
        {
            new AccountCreatedEvent(accountId, "Eve", 100m)
                .ToDomainEvent()
                .WithTag("accountId", accountId.ToString())
                .Build()
        };

        await _eventStore.AppendAsync(events, null);
        await _projectionRebuilder.RebuildAsync("AccountBalance");

        // Act — perform a normal (non-rebuild) incremental update via the manager
        var depositEvent = new MoneyDepositedEvent(accountId, 50m)
            .ToDomainEvent()
            .WithTag("accountId", accountId.ToString())
            .Build();
        await _eventStore.AppendAsync([depositEvent], null);

        // Read back the new event(s) that sit after the rebuild checkpoint
        var allEvents = await _eventStore.ReadAsync(Query.All(), null);
        var newEvents = allEvents.OrderBy(e => e.Position).Skip(1).ToArray();

        await _projectionManager.UpdateAsync(newEvents);

        // Assert — state is correct
        var store = _serviceProvider.GetRequiredService<IProjectionStore<AccountBalanceState>>();
        var state = await store.GetAsync(accountId.ToString());

        Assert.NotNull(state);
        Assert.Equal(150m, state.Balance); // 100 + 50

        // Assert — metadata index file was re-created by the normal SaveAsync path
        var metadataIndexFile = Path.Combine(_projectionPath, "Metadata", "index.json");
        Assert.True(File.Exists(metadataIndexFile),
            "Metadata/index.json should be re-created by normal SaveAsync after rebuild.");

        // Assert — the metadata version should reflect a clean start (not stale pre-rebuild data)
        var indexJson = await File.ReadAllTextAsync(metadataIndexFile);
        var index = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ProjectionMetadata>>(indexJson);
        Assert.NotNull(index);
        Assert.True(index.ContainsKey(accountId.ToString()));

        // The first normal SaveAsync after rebuild sees no cached metadata and no index file,
        // so it creates metadata with Version = 1.
        Assert.Equal(1, index[accountId.ToString()].Version);
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();

        if (Directory.Exists(_testStoragePath))
        {
            try { Directory.Delete(_testStoragePath, recursive: true); }
            catch { /* ignore cleanup errors */ }
        }
    }
}
