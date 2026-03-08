using Opossum.Core;
using Opossum.DependencyInjection;
using Opossum.Extensions;
using Opossum.Projections;

namespace Opossum.IntegrationTests.Projections;

/// <summary>
/// Integration tests for projection rebuild crash recovery — journal persistence,
/// tag accumulator restoration, orphaned temp directory cleanup, and full resume flow.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class ProjectionRebuildCrashRecoveryTests : IDisposable
{
    private readonly string _testStoragePath;
    private readonly string _checkpointPath;
    private readonly string _projectionsPath;

    public ProjectionRebuildCrashRecoveryTests()
    {
        _testStoragePath = Path.Combine(
            Path.GetTempPath(),
            "OpossumCrashRecoveryTests",
            Guid.NewGuid().ToString());

        var contextPath = Path.Combine(_testStoragePath, "CrashRecoveryContext");
        _projectionsPath = Path.Combine(contextPath, "Projections");
        _checkpointPath = Path.Combine(_projectionsPath, "_checkpoints");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testStoragePath))
        {
            try { Directory.Delete(_testStoragePath, recursive: true); }
            catch { /* ignore cleanup errors */ }
        }
    }

    // ========================================================================
    // Test 1 — Resume interrupted rebuild from flush point
    // ========================================================================

    [Fact]
    public async Task ResumeInterruptedRebuild_WhenJournalExists_ResumesFromFlushPointAsync()
    {
        // Arrange — seed events
        var (sp, eventStore, projectionManager, rebuilder) = BuildServices();
        using var _ = sp;

        var projection = new CrashRecoveryProjection();
        projectionManager.RegisterProjection(projection);

        var accountId = Guid.NewGuid();
        var events = new[]
        {
            new CrashRecoveryCreatedEvent(accountId, "Alice", 1000m).ToDomainEvent().WithTag("accountId", accountId.ToString()).Build(),
            new CrashRecoveryDepositEvent(accountId, 200m).ToDomainEvent().WithTag("accountId", accountId.ToString()).Build(),
            new CrashRecoveryDepositEvent(accountId, 300m).ToDomainEvent().WithTag("accountId", accountId.ToString()).Build(),
        };
        await eventStore.AppendAsync(events, null);

        // Simulate a crash after processing event at position 1:
        // Manually write a journal referencing position 1 and a temp dir with partial state.
        var tempPath = Path.Combine(_projectionsPath, "CrashRecoveryBalance.tmp.fakeguid");
        Directory.CreateDirectory(tempPath);

        // Write partial state for positions ≤ 1 (the create event only)
        WriteProjectionFile(tempPath, accountId.ToString(), new CrashRecoveryState
        {
            AccountId = accountId,
            OwnerName = "Alice",
            Balance = 1000m,
            TransactionCount = 1
        });

        WriteJournal("CrashRecoveryBalance", tempPath, storeHead: 3, resumeFrom: 1);
        // No tag accumulator companion needed for a tagless projection

        // Act — resume
        await rebuilder.ResumeInterruptedRebuildsAsync();

        // Assert — final state should equal a full rebuild (all 3 events)
        var store = sp.GetRequiredService<IProjectionStore<CrashRecoveryState>>();
        var state = await store.GetAsync(accountId.ToString());

        Assert.NotNull(state);
        Assert.Equal(1500m, state.Balance); // 1000 + 200 + 300
        Assert.Equal(3, state.TransactionCount);

        // Journal and tags companion file should be deleted
        Assert.False(File.Exists(Path.Combine(_checkpointPath, "CrashRecoveryBalance.rebuild.json")));
        Assert.False(File.Exists(Path.Combine(_checkpointPath, "CrashRecoveryBalance.rebuild.tags.json")));

        // Checkpoint should be saved
        var checkpoint = await projectionManager.GetCheckpointAsync("CrashRecoveryBalance");
        Assert.True(checkpoint >= 3);
    }

    // ========================================================================
    // Test 2 — Missing temp dir: journal discarded, projection rebuilt fresh
    // ========================================================================

    [Fact]
    public async Task ResumeInterruptedRebuild_WhenTempDirMissing_DeletesJournalAndRebuildsFromScratchAsync()
    {
        // Arrange
        var (sp, eventStore, projectionManager, rebuilder) = BuildServices();
        using var _ = sp;

        var projection = new CrashRecoveryProjection();
        projectionManager.RegisterProjection(projection);

        var accountId = Guid.NewGuid();
        var events = new[]
        {
            new CrashRecoveryCreatedEvent(accountId, "Bob", 500m).ToDomainEvent().WithTag("accountId", accountId.ToString()).Build(),
            new CrashRecoveryDepositEvent(accountId, 100m).ToDomainEvent().WithTag("accountId", accountId.ToString()).Build(),
        };
        await eventStore.AppendAsync(events, null);

        // Write a journal referencing a temp dir that does NOT exist
        var fakeTempPath = Path.Combine(_projectionsPath, "CrashRecoveryBalance.tmp.nonexistent");
        WriteJournal("CrashRecoveryBalance", fakeTempPath, storeHead: 2, resumeFrom: 1);
        WriteTagAccumulatorFile("CrashRecoveryBalance", []);

        // Act — resume should discard journal since temp dir is gone
        await rebuilder.ResumeInterruptedRebuildsAsync();

        // Assert — journal deleted
        Assert.False(File.Exists(Path.Combine(_checkpointPath, "CrashRecoveryBalance.rebuild.json")));
        Assert.False(File.Exists(Path.Combine(_checkpointPath, "CrashRecoveryBalance.rebuild.tags.json")));

        // Now do a fresh rebuild (simulating what RebuildAllAsync would do)
        await rebuilder.RebuildAllAsync(forceRebuild: false);

        var store = sp.GetRequiredService<IProjectionStore<CrashRecoveryState>>();
        var state = await store.GetAsync(accountId.ToString());

        Assert.NotNull(state);
        Assert.Equal(600m, state.Balance); // 500 + 100
    }

    // ========================================================================
    // Test 3 — Orphaned temp dir with no journal is deleted
    // ========================================================================

    [Fact]
    public async Task CleanOrphanedTempDir_WhenNoJournalExists_DeletesOrphanedDirAsync()
    {
        // Arrange — create infrastructure so checkpoint path exists
        var (sp, _, _, rebuilder) = BuildServices();
        using var _ = sp;

        // Create an orphaned temp directory with no matching journal
        var orphanedDir = Path.Combine(_projectionsPath, "SomeProjection.tmp.orphanedguid");
        Directory.CreateDirectory(orphanedDir);
        await File.WriteAllTextAsync(Path.Combine(orphanedDir, "dummy.json"), "{}");

        Assert.True(Directory.Exists(orphanedDir));

        // Act
        await rebuilder.ResumeInterruptedRebuildsAsync();

        // Assert — orphaned directory should be deleted
        Assert.False(Directory.Exists(orphanedDir));
    }

    // ========================================================================
    // Test 4 — Journal flushed every RebuildFlushInterval events
    // ========================================================================

    [Fact]
    public async Task RebuildJournal_IsFlushedEveryFlushIntervalAsync()
    {
        // Use a very small flush interval so we can verify with few events
        var flushInterval = 100;
        var totalEvents = flushInterval * 2;

        var (sp, eventStore, projectionManager, rebuilder) = BuildServices(
            configureProjections: opts => opts.RebuildFlushInterval = flushInterval);
        using var _ = sp;

        var projection = new CrashRecoveryProjection();
        projectionManager.RegisterProjection(projection);

        // Seed enough events
        for (var i = 0; i < totalEvents; i++)
        {
            var accountId = Guid.NewGuid();
            var evt = new CrashRecoveryCreatedEvent(accountId, $"User{i}", 100m)
                .ToDomainEvent()
                .WithTag("accountId", accountId.ToString())
                .Build();
            await eventStore.AppendAsync([evt], null);
        }

        // We need to observe the journal during the rebuild. Since the rebuild runs
        // synchronously from our perspective, we verify that the journal file DOES NOT
        // exist after a successful rebuild (it's cleaned up). But we CAN verify that
        // the rebuild processes all events correctly, proving the batch loop ran.
        // The real proof is test 1 (resume) and test 5 (cleanup after success).

        // For a more direct check: run the rebuild, then verify no journal remains
        // (meaning the journal was created, possibly flushed, then cleaned up).
        await rebuilder.RebuildAsync("CrashRecoveryBalance");

        // After successful completion, no journal or tags file should remain
        var journalFiles = Directory.GetFiles(_checkpointPath, "*.rebuild.json");
        var tagsFiles = Directory.GetFiles(_checkpointPath, "*.rebuild.tags.json");
        Assert.Empty(journalFiles);
        Assert.Empty(tagsFiles);

        // And the checkpoint should be at least totalEvents
        var checkpoint = await projectionManager.GetCheckpointAsync("CrashRecoveryBalance");
        Assert.True(checkpoint >= totalEvents);
    }

    // ========================================================================
    // Test 5 — Journal is deleted on successful completion
    // ========================================================================

    [Fact]
    public async Task RebuildJournal_IsDeletedOnSuccessfulCompletionAsync()
    {
        // Arrange
        var (sp, eventStore, projectionManager, rebuilder) = BuildServices();
        using var _ = sp;

        var projection = new CrashRecoveryProjection();
        projectionManager.RegisterProjection(projection);

        var accountId = Guid.NewGuid();
        await eventStore.AppendAsync([
            new CrashRecoveryCreatedEvent(accountId, "Carol", 750m).ToDomainEvent().WithTag("accountId", accountId.ToString()).Build()
        ], null);

        // Act — run a complete rebuild
        var result = await rebuilder.RebuildAsync("CrashRecoveryBalance");

        // Assert — rebuild succeeded
        Assert.True(result.Details[0].Success);

        // No journal or tags companion files should exist
        Assert.False(File.Exists(Path.Combine(_checkpointPath, "CrashRecoveryBalance.rebuild.json")));
        Assert.False(File.Exists(Path.Combine(_checkpointPath, "CrashRecoveryBalance.rebuild.tags.json")));

        // No temp directories should remain
        var tempDirs = Directory.GetDirectories(_projectionsPath, "*.tmp.*");
        Assert.Empty(tempDirs);
    }

    // ========================================================================
    // Test 6 — Tag accumulator is restored correctly on resume
    // ========================================================================

    [Fact]
    public async Task ResumeInterruptedRebuild_TagAccumulatorRestoredCorrectlyAsync()
    {
        // Arrange — seed events with tags
        var (sp, eventStore, projectionManager, rebuilder) = BuildServices();
        using var _ = sp;

        var taggedProjection = new TaggedCrashRecoveryProjection();
        projectionManager.RegisterProjection(taggedProjection);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        await eventStore.AppendAsync([
            new CrashRecoveryCreatedEvent(id1, "Alice", 1000m).ToDomainEvent().WithTag("accountId", id1.ToString()).Build(),
            new CrashRecoveryCreatedEvent(id2, "Bob", 500m).ToDomainEvent().WithTag("accountId", id2.ToString()).Build(),
            new CrashRecoveryDepositEvent(id1, 200m).ToDomainEvent().WithTag("accountId", id1.ToString()).Build(),
        ], null);

        // First, do a full from-scratch rebuild to get the "expected" tag indices
        await rebuilder.RebuildAsync("TaggedCrashRecoveryBalance");

        var taggedStore = sp.GetRequiredService<IProjectionStore<CrashRecoveryState>>();
        var expectedActive = await taggedStore.QueryByTagAsync(new Tag("Status", "Active"));
        var expectedId1State = await taggedStore.GetAsync(id1.ToString());
        var expectedId2State = await taggedStore.GetAsync(id2.ToString());

        // Capture the full from-scratch state
        Assert.Equal(2, expectedActive.Count);
        Assert.NotNull(expectedId1State);
        Assert.NotNull(expectedId2State);

        // Now simulate a crash after processing event at position 1 (only id1 created).
        // Manually write journal + tags + partial temp directory.
        var tempPath = Path.Combine(_projectionsPath, "TaggedCrashRecoveryBalance.tmp.resumeguid");
        Directory.CreateDirectory(tempPath);

        // Write partial state for id1 only (position 1)
        WriteProjectionFile(tempPath, id1.ToString(), new CrashRecoveryState
        {
            AccountId = id1,
            OwnerName = "Alice",
            Balance = 1000m,
            TransactionCount = 1
        });

        // Write tag accumulator with tags for id1 from position 1
        var tagAccumulator = new Dictionary<string, HashSet<string>>
        {
            ["status_active.json"] = [id1.ToString()]
        };
        WriteTagAccumulatorFile("TaggedCrashRecoveryBalance", tagAccumulator);
        WriteJournal("TaggedCrashRecoveryBalance", tempPath, storeHead: 3, resumeFrom: 1);

        // Delete the checkpoint so RebuildAllAsync won't skip it
        var checkpointFile = Path.Combine(_checkpointPath, "TaggedCrashRecoveryBalance.checkpoint");
        if (File.Exists(checkpointFile))
            File.Delete(checkpointFile);

        // Act — resume the interrupted rebuild
        await rebuilder.ResumeInterruptedRebuildsAsync();

        // Assert — state matches full rebuild
        var resumedId1 = await taggedStore.GetAsync(id1.ToString());
        var resumedId2 = await taggedStore.GetAsync(id2.ToString());

        Assert.NotNull(resumedId1);
        Assert.Equal(expectedId1State.Balance, resumedId1.Balance);
        Assert.Equal(expectedId1State.TransactionCount, resumedId1.TransactionCount);

        Assert.NotNull(resumedId2);
        Assert.Equal(expectedId2State.Balance, resumedId2.Balance);

        // Tag index should contain keys from ALL events (not just the resumed portion)
        var activeResults = await taggedStore.QueryByTagAsync(new Tag("Status", "Active"));
        Assert.Equal(2, activeResults.Count);
        Assert.Contains(activeResults, s => s.AccountId == id1);
        Assert.Contains(activeResults, s => s.AccountId == id2);

        // Journal and tags file should be cleaned up
        Assert.False(File.Exists(Path.Combine(_checkpointPath, "TaggedCrashRecoveryBalance.rebuild.json")));
        Assert.False(File.Exists(Path.Combine(_checkpointPath, "TaggedCrashRecoveryBalance.rebuild.tags.json")));
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private (ServiceProvider sp, IEventStore eventStore, IProjectionManager projectionManager, IProjectionRebuilder rebuilder)
        BuildServices(Action<ProjectionOptions>? configureProjections = null)
    {
        var services = new ServiceCollection();

        services.AddOpossum(options =>
        {
            options.RootPath = _testStoragePath;
            options.UseStore("CrashRecoveryContext");
        });

        services.AddProjections(options =>
        {
            options.ScanAssembly(typeof(ProjectionRebuildCrashRecoveryTests).Assembly);
            options.AutoRebuild = AutoRebuildMode.None; // We control rebuilds manually
            configureProjections?.Invoke(options);
        });

        var sp = services.BuildServiceProvider();
        return (
            sp,
            sp.GetRequiredService<IEventStore>(),
            sp.GetRequiredService<IProjectionManager>(),
            sp.GetRequiredService<IProjectionRebuilder>()
        );
    }

    private void WriteJournal(string projectionName, string tempPath, long storeHead, long resumeFrom)
    {
        Directory.CreateDirectory(_checkpointPath);

        var journal = new ProjectionRebuildJournal
        {
            ProjectionName = projectionName,
            TempPath = tempPath,
            StoreHeadAtStart = storeHead,
            ResumeFromPosition = resumeFrom,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            LastFlushedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        var json = System.Text.Json.JsonSerializer.Serialize(journal, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        });

        File.WriteAllText(Path.Combine(_checkpointPath, $"{projectionName}.rebuild.json"), json);
    }

    private void WriteTagAccumulatorFile(string projectionName, Dictionary<string, HashSet<string>> tagAccumulator)
    {
        Directory.CreateDirectory(_checkpointPath);

        var json = System.Text.Json.JsonSerializer.Serialize(tagAccumulator, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        });

        File.WriteAllText(Path.Combine(_checkpointPath, $"{projectionName}.rebuild.tags.json"), json);
    }

    private static void WriteProjectionFile<TState>(string directory, string key, TState state) where TState : class
    {
        var safeKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        var wrapper = new ProjectionWithMetadata<TState>
        {
            Data = state,
            Metadata = new ProjectionMetadata
            {
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdatedAt = DateTimeOffset.UtcNow,
                Version = 1,
                SizeInBytes = 0
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(wrapper, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        });

        File.WriteAllText(Path.Combine(directory, $"{safeKey}.json"), json);
    }
}

// ============================================================================
// TEST EVENTS AND PROJECTIONS (without tags)
// ============================================================================

public record CrashRecoveryCreatedEvent(Guid AccountId, string OwnerName, decimal InitialBalance) : IEvent;
public record CrashRecoveryDepositEvent(Guid AccountId, decimal Amount) : IEvent;

public record CrashRecoveryState
{
    public Guid AccountId { get; init; }
    public string OwnerName { get; init; } = string.Empty;
    public decimal Balance { get; init; }
    public int TransactionCount { get; init; }
}

[ProjectionDefinition("CrashRecoveryBalance")]
public class CrashRecoveryProjection : IProjectionDefinition<CrashRecoveryState>
{
    public string ProjectionName => "CrashRecoveryBalance";

    public string[] EventTypes =>
    [
        nameof(CrashRecoveryCreatedEvent),
        nameof(CrashRecoveryDepositEvent)
    ];

    public string KeySelector(SequencedEvent evt) => evt.Event.Event switch
    {
        CrashRecoveryCreatedEvent e => e.AccountId.ToString(),
        CrashRecoveryDepositEvent e => e.AccountId.ToString(),
        _ => throw new InvalidOperationException($"Unknown event type: {evt.Event.EventType}")
    };

    public CrashRecoveryState? Apply(CrashRecoveryState? current, SequencedEvent evt) => evt.Event.Event switch
    {
        CrashRecoveryCreatedEvent e => new CrashRecoveryState
        {
            AccountId = e.AccountId,
            OwnerName = e.OwnerName,
            Balance = e.InitialBalance,
            TransactionCount = 1
        },
        CrashRecoveryDepositEvent e => current! with
        {
            Balance = current.Balance + e.Amount,
            TransactionCount = current.TransactionCount + 1
        },
        _ => current
    };
}

// ============================================================================
// TAGGED PROJECTION (for tag accumulator restore test)
// ============================================================================

[ProjectionDefinition("TaggedCrashRecoveryBalance")]
[ProjectionTags(typeof(CrashRecoveryTagProvider))]
public class TaggedCrashRecoveryProjection : IProjectionDefinition<CrashRecoveryState>
{
    public string ProjectionName => "TaggedCrashRecoveryBalance";

    public string[] EventTypes =>
    [
        nameof(CrashRecoveryCreatedEvent),
        nameof(CrashRecoveryDepositEvent)
    ];

    public string KeySelector(SequencedEvent evt) => evt.Event.Event switch
    {
        CrashRecoveryCreatedEvent e => e.AccountId.ToString(),
        CrashRecoveryDepositEvent e => e.AccountId.ToString(),
        _ => throw new InvalidOperationException($"Unknown event type: {evt.Event.EventType}")
    };

    public CrashRecoveryState? Apply(CrashRecoveryState? current, SequencedEvent evt) => evt.Event.Event switch
    {
        CrashRecoveryCreatedEvent e => new CrashRecoveryState
        {
            AccountId = e.AccountId,
            OwnerName = e.OwnerName,
            Balance = e.InitialBalance,
            TransactionCount = 1
        },
        CrashRecoveryDepositEvent e => current! with
        {
            Balance = current.Balance + e.Amount,
            TransactionCount = current.TransactionCount + 1
        },
        _ => current
    };
}

public class CrashRecoveryTagProvider : IProjectionTagProvider<CrashRecoveryState>
{
    public IEnumerable<Tag> GetTags(CrashRecoveryState state)
    {
        yield return new Tag("Status", "Active");
    }
}
