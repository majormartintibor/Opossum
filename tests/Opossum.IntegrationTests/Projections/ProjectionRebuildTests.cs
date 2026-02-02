using Microsoft.Extensions.DependencyInjection;
using Opossum.Core;
using Opossum.DependencyInjection;
using Opossum.Extensions;
using Opossum.Projections;

namespace Opossum.IntegrationTests.Projections;

/// <summary>
/// Integration tests for projection rebuild, checkpoint management, and recovery scenarios.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class ProjectionRebuildTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testStoragePath;
    private readonly IEventStore _eventStore;
    private readonly IProjectionManager _projectionManager;

    public ProjectionRebuildTests()
    {
        _testStoragePath = Path.Combine(
            Path.GetTempPath(),
            "OpossumProjectionRebuildTests",
            Guid.NewGuid().ToString());

        var services = new ServiceCollection();

        services.AddOpossum(options =>
        {
            options.RootPath = _testStoragePath;
            options.AddContext("RebuildContext");
        });

        services.AddProjections(options =>
        {
            options.ScanAssembly(typeof(ProjectionRebuildTests).Assembly);
        });

        _serviceProvider = services.BuildServiceProvider();
        _eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        _projectionManager = _serviceProvider.GetRequiredService<IProjectionManager>();
    }

    [Fact]
    public async Task RebuildProjection_AfterEventsAdded_BuildsCorrectState()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var projection = new AccountBalanceProjection();
        _projectionManager.RegisterProjection(projection);

        // Add events before rebuild
        var events = new[]
        {
            new AccountCreatedEvent(accountId, "John Doe", 1000m)
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

        // Act - Rebuild projection
        await _projectionManager.RebuildAsync("AccountBalance");

        // Assert
        var store = _serviceProvider.GetRequiredService<IProjectionStore<AccountBalanceState>>();
        var account = await store.GetAsync(accountId.ToString());

        Assert.NotNull(account);
        Assert.Equal(accountId, account.AccountId);
        Assert.Equal("John Doe", account.OwnerName);
        Assert.Equal(1300m, account.Balance); // 1000 + 500 - 200
        Assert.Equal(3, account.TransactionCount);
    }

    [Fact]
    public async Task RebuildProjection_WithExistingState_ReplacesState()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var projection = new AccountBalanceProjection();
        _projectionManager.RegisterProjection(projection);

        // First build
        var initialEvents = new[]
        {
            new AccountCreatedEvent(accountId, "Initial Name", 100m)
                .ToDomainEvent()
                .WithTag("accountId", accountId.ToString())
                .Build()
        };

        await _eventStore.AppendAsync(initialEvents, null);
        await _projectionManager.RebuildAsync("AccountBalance");

        var store = _serviceProvider.GetRequiredService<IProjectionStore<AccountBalanceState>>();
        var initialState = await store.GetAsync(accountId.ToString());
        Assert.Equal(100m, initialState!.Balance);

        // Add more events
        var newEvents = new[]
        {
            new MoneyDepositedEvent(accountId, 900m)
                .ToDomainEvent()
                .WithTag("accountId", accountId.ToString())
                .Build()
        };

        await _eventStore.AppendAsync(newEvents, null);

        // Act - Rebuild again
        await _projectionManager.RebuildAsync("AccountBalance");

        // Assert - State should be completely rebuilt
        var rebuiltState = await store.GetAsync(accountId.ToString());
        Assert.Equal(1000m, rebuiltState!.Balance); // 100 + 900
        Assert.Equal(2, rebuiltState.TransactionCount);
    }

    [Fact]
    public async Task RebuildProjection_WithMultipleInstances_BuildsAll()
    {
        // Arrange
        var projection = new AccountBalanceProjection();
        _projectionManager.RegisterProjection(projection);

        var accountIds = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();

        // Create 10 accounts
        var events = accountIds
            .Select((id, index) => new AccountCreatedEvent(id, $"Account {index}", 100m * (index + 1))
                .ToDomainEvent()
                .WithTag("accountId", id.ToString())
                .Build())
            .ToArray();

        await _eventStore.AppendAsync(events, null);

        // Act - Rebuild
        await _projectionManager.RebuildAsync("AccountBalance");

        // Assert - All accounts should be built
        var store = _serviceProvider.GetRequiredService<IProjectionStore<AccountBalanceState>>();
        
        for (int i = 0; i < accountIds.Count; i++)
        {
            var account = await store.GetAsync(accountIds[i].ToString());
            Assert.NotNull(account);
            Assert.Equal(100m * (i + 1), account.Balance);
        }
    }

    [Fact]
    public async Task RebuildProjection_WithDeletion_RemovesProjection()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var projection = new AccountBalanceProjection();
        _projectionManager.RegisterProjection(projection);

        var events = new[]
        {
            new AccountCreatedEvent(accountId, "Test", 1000m)
                .ToDomainEvent()
                .WithTag("accountId", accountId.ToString())
                .Build(),
            new AccountClosedEvent(accountId)
                .ToDomainEvent()
                .WithTag("accountId", accountId.ToString())
                .Build()
        };

        await _eventStore.AppendAsync(events, null);

        // Act - Rebuild
        await _projectionManager.RebuildAsync("AccountBalance");

        // Assert - Projection should not exist (Apply returned null)
        var store = _serviceProvider.GetRequiredService<IProjectionStore<AccountBalanceState>>();
        var account = await store.GetAsync(accountId.ToString());

        Assert.Null(account);
    }

    [Fact]
    public async Task RebuildProjection_WithEventOrdering_ProcessesInOrder()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var projection = new AccountBalanceProjection();
        _projectionManager.RegisterProjection(projection);

        // Add events that must be processed in order
        var events = new[]
        {
            new AccountCreatedEvent(accountId, "Test", 0m)
                .ToDomainEvent()
                .WithTag("accountId", accountId.ToString())
                .Build(),
            new MoneyDepositedEvent(accountId, 100m)
                .ToDomainEvent()
                .WithTag("accountId", accountId.ToString())
                .Build(),
            new MoneyWithdrawnEvent(accountId, 50m)
                .ToDomainEvent()
                .WithTag("accountId", accountId.ToString())
                .Build(),
            new MoneyDepositedEvent(accountId, 75m)
                .ToDomainEvent()
                .WithTag("accountId", accountId.ToString())
                .Build()
        };

        await _eventStore.AppendAsync(events, null);

        // Act
        await _projectionManager.RebuildAsync("AccountBalance");

        // Assert - Final balance should reflect correct order
        var store = _serviceProvider.GetRequiredService<IProjectionStore<AccountBalanceState>>();
        var account = await store.GetAsync(accountId.ToString());

        Assert.NotNull(account);
        Assert.Equal(125m, account.Balance); // 0 + 100 - 50 + 75
        Assert.Equal(4, account.TransactionCount);
    }

    [Fact]
    public async Task RebuildProjection_WithNoEvents_CreatesNoInstances()
    {
        // Arrange
        var projection = new AccountBalanceProjection();
        _projectionManager.RegisterProjection(projection);

        // Act - Rebuild with no events
        await _projectionManager.RebuildAsync("AccountBalance");

        // Assert - No projections should exist
        var store = _serviceProvider.GetRequiredService<IProjectionStore<AccountBalanceState>>();
        var allAccounts = await store.GetAllAsync();

        Assert.Empty(allAccounts);
    }

    [Fact]
    public async Task RebuildProjection_WithPartialBatches_ProcessesAllEvents()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var projection = new AccountBalanceProjection();
        _projectionManager.RegisterProjection(projection);

        // Add many events (more than default batch size)
        var eventCount = 150;
        var depositEvents = Enumerable.Range(0, eventCount)
            .Select(i => new MoneyDepositedEvent(accountId, 1m)
                .ToDomainEvent()
                .WithTag("accountId", accountId.ToString())
                .Build())
            .ToList();

        // Add account creation first
        var createEvent = new AccountCreatedEvent(accountId, "Test", 0m)
            .ToDomainEvent()
            .WithTag("accountId", accountId.ToString())
            .Build();

        await _eventStore.AppendAsync([createEvent], null);
        await _eventStore.AppendAsync(depositEvents.ToArray(), null);

        // Act - Rebuild (should process in batches)
        await _projectionManager.RebuildAsync("AccountBalance");

        // Assert - All events should be processed
        var store = _serviceProvider.GetRequiredService<IProjectionStore<AccountBalanceState>>();
        var account = await store.GetAsync(accountId.ToString());

        Assert.NotNull(account);
        Assert.Equal(eventCount, account.Balance);
        Assert.Equal(eventCount + 1, account.TransactionCount); // +1 for creation
    }

    [Fact]
    public async Task GetCheckpoint_ReturnsLastProcessedPosition()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var projection = new AccountBalanceProjection();
        _projectionManager.RegisterProjection(projection);

        var events = new[]
        {
            new AccountCreatedEvent(accountId, "Test", 100m)
                .ToDomainEvent()
                .WithTag("accountId", accountId.ToString())
                .Build(),
            new MoneyDepositedEvent(accountId, 50m)
                .ToDomainEvent()
                .WithTag("accountId", accountId.ToString())
                .Build()
        };

        await _eventStore.AppendAsync(events, null);
        await _projectionManager.RebuildAsync("AccountBalance");

        // Act
        var checkpoint = await _projectionManager.GetCheckpointAsync("AccountBalance");

        // Assert - Should be position 2 (last event)
        Assert.Equal(2, checkpoint);
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();

        if (Directory.Exists(_testStoragePath))
        {
            try
            {
                Directory.Delete(_testStoragePath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}

// ============================================================================
// TEST EVENTS AND PROJECTION
// ============================================================================

public record AccountCreatedEvent(Guid AccountId, string OwnerName, decimal InitialBalance) : IEvent;
public record MoneyDepositedEvent(Guid AccountId, decimal Amount) : IEvent;
public record MoneyWithdrawnEvent(Guid AccountId, decimal Amount) : IEvent;
public record AccountClosedEvent(Guid AccountId) : IEvent;

public record AccountBalanceState
{
    public Guid AccountId { get; init; }
    public string OwnerName { get; init; } = string.Empty;
    public decimal Balance { get; init; }
    public int TransactionCount { get; init; }
}

[ProjectionDefinition("AccountBalance")]
public class AccountBalanceProjection : IProjectionDefinition<AccountBalanceState>
{
    public string ProjectionName => "AccountBalance";

    public string[] EventTypes => [
        nameof(AccountCreatedEvent),
        nameof(MoneyDepositedEvent),
        nameof(MoneyWithdrawnEvent),
        nameof(AccountClosedEvent)
    ];

    public string KeySelector(SequencedEvent evt)
    {
        return evt.Event.Event switch
        {
            AccountCreatedEvent ace => ace.AccountId.ToString(),
            MoneyDepositedEvent mde => mde.AccountId.ToString(),
            MoneyWithdrawnEvent mwe => mwe.AccountId.ToString(),
            AccountClosedEvent ace => ace.AccountId.ToString(),
            _ => throw new InvalidOperationException($"Unknown event type: {evt.Event.EventType}")
        };
    }

    public AccountBalanceState? Apply(AccountBalanceState? current, IEvent evt)
    {
        return evt switch
        {
            AccountCreatedEvent ace => new AccountBalanceState
            {
                AccountId = ace.AccountId,
                OwnerName = ace.OwnerName,
                Balance = ace.InitialBalance,
                TransactionCount = 1
            },
            MoneyDepositedEvent mde => current! with
            {
                Balance = current.Balance + mde.Amount,
                TransactionCount = current.TransactionCount + 1
            },
            MoneyWithdrawnEvent mwe => current! with
            {
                Balance = current.Balance - mwe.Amount,
                TransactionCount = current.TransactionCount + 1
            },
            AccountClosedEvent => null, // Delete projection
            _ => current
        };
    }
}
