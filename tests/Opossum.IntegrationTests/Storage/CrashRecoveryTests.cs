using Opossum.Configuration;
using Opossum.Core;
using Opossum.Extensions;
using Opossum.IntegrationTests.Helpers;
using Opossum.Storage.FileSystem;

namespace Opossum.IntegrationTests.Storage;

/// <summary>
/// Integration tests that verify crash-recovery behaviour when event files are
/// written (step 7) but the ledger is not updated (step 9) before a process crash.
///
/// Each test uses its own isolated temp directory so tests can run in parallel.
/// </summary>
public sealed class CrashRecoveryTests : IDisposable
{
    private readonly string _tempPath;

    private sealed record OrphanedEvent(string Data) : IEvent;
    private sealed record PostRecoveryEvent(string Data) : IEvent;

    public CrashRecoveryTests()
    {
        _tempPath = Path.Combine(
            Path.GetTempPath(),
            "OpossumCrashRecoveryTests",
            Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        TestDirectoryHelper.ForceDelete(_tempPath);
    }

    /// <summary>
    /// Task 10: Simulate a step-7-only crash (write event files, don't update ledger),
    /// then call AppendAsync — verify original events are preserved and new events get
    /// positions after the orphaned ones.
    /// </summary>
    [Fact]
    public async Task AppendAsync_AfterCrash_PreservesOrphanedEvents_AndAllocatesNextPositionsAsync()
    {
        // ── Arrange: append 2 events normally ──
        var options = CreateOptions();
        using var store = new FileSystemEventStore(options);

        var preEvent1 = new OrphanedEvent("pre-crash-1")
            .ToDomainEvent().Build();
        var preEvent2 = new OrphanedEvent("pre-crash-2")
            .ToDomainEvent().Build();

        await store.AppendAsync([preEvent1, preEvent2], condition: null);

        // Verify: ledger at position 2
        var eventsBeforeCrash = await store.ReadAsync(Query.All());
        Assert.Equal(2, eventsBeforeCrash.Length);

        // ── Simulate crash: write event file at position 3 without updating ledger ──
        var eventFileManager = new EventFileManager(flushImmediately: false, writeProtect: false);
        var contextPath = Path.Combine(_tempPath, options.StoreName!);
        var eventsPath = Path.Combine(contextPath, "events");

        var orphanedSequenced = new SequencedEvent
        {
            Position = 3,
            Event = new DomainEvent { Event = new OrphanedEvent("orphaned-crash-event") },
            Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
        };
        await eventFileManager.WriteEventAsync(eventsPath, orphanedSequenced, allowOverwrite: true);

        // Verify precondition: file exists at position 3, but ledger still at 2
        Assert.True(File.Exists(Path.Combine(eventsPath, "0000000003.json")));
        var ledgerManager = new LedgerManager(flushImmediately: false);
        var ledgerPosition = await ledgerManager.GetLastSequencePositionAsync(contextPath);
        Assert.Equal(2, ledgerPosition);

        // ── Act: create a NEW store instance (simulates restart) and append ──
        using var recoveredStore = new FileSystemEventStore(CreateOptions());

        var postEvent = new PostRecoveryEvent("post-recovery")
            .ToDomainEvent().Build();

        await recoveredStore.AppendAsync([postEvent], condition: null);

        // ── Assert: orphaned event at position 3 is preserved, new event at position 4 ──
        var allEvents = await recoveredStore.ReadAsync(Query.All());

        Assert.Equal(4, allEvents.Length);
        Assert.Equal(1, allEvents[0].Position);
        Assert.Equal(2, allEvents[1].Position);
        Assert.Equal(3, allEvents[2].Position);
        Assert.Equal(4, allEvents[3].Position);

        // The orphaned event at position 3 still has its original data
        var orphanRead = (OrphanedEvent)allEvents[2].Event.Event;
        Assert.Equal("orphaned-crash-event", orphanRead.Data);

        // The new event is at position 4
        var postRead = (PostRecoveryEvent)allEvents[3].Event.Event;
        Assert.Equal("post-recovery", postRead.Data);
    }

    /// <summary>
    /// Task 11: After a crash-and-recover scenario, ReadAsync returns events from both
    /// the orphaned-but-reconciled positions and the newly-appended positions, in correct
    /// ascending order.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AfterCrashRecovery_ReturnsAllEventsInCorrectOrderAsync()
    {
        // ── Arrange: append 3 events normally ──
        var options = CreateOptions();
        using var store = new FileSystemEventStore(options);

        var events = Enumerable.Range(1, 3)
            .Select(i => new OrphanedEvent($"normal-{i}").ToDomainEvent().Build())
            .ToArray();

        await store.AppendAsync(events, condition: null);

        // ── Simulate crash: write 2 orphaned event files at positions 4 and 5 ──
        var eventFileManager = new EventFileManager(flushImmediately: false, writeProtect: false);
        var contextPath = Path.Combine(_tempPath, options.StoreName!);
        var eventsPath = Path.Combine(contextPath, "events");

        for (int i = 4; i <= 5; i++)
        {
            var orphan = new SequencedEvent
            {
                Position = i,
                Event = new DomainEvent { Event = new OrphanedEvent($"orphaned-{i}") },
                Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
            };
            await eventFileManager.WriteEventAsync(eventsPath, orphan, allowOverwrite: true);
        }

        // ── Act: create a new store (restart), append 2 more events ──
        using var recoveredStore = new FileSystemEventStore(CreateOptions());

        var postEvents = Enumerable.Range(1, 2)
            .Select(i => new PostRecoveryEvent($"post-{i}").ToDomainEvent().Build())
            .ToArray();

        await recoveredStore.AppendAsync(postEvents, condition: null);

        // ── Assert: ReadAsync returns all 7 events in ascending position order ──
        var allEvents = await recoveredStore.ReadAsync(Query.All());

        Assert.Equal(7, allEvents.Length);
        for (int i = 0; i < allEvents.Length; i++)
        {
            Assert.Equal(i + 1, allEvents[i].Position);
        }

        // Verify data integrity for each segment
        for (int i = 0; i < 3; i++)
        {
            var data = ((OrphanedEvent)allEvents[i].Event.Event).Data;
            Assert.Equal($"normal-{i + 1}", data);
        }

        for (int i = 3; i < 5; i++)
        {
            var data = ((OrphanedEvent)allEvents[i].Event.Event).Data;
            Assert.Equal($"orphaned-{i + 1}", data);
        }

        for (int i = 5; i < 7; i++)
        {
            var data = ((PostRecoveryEvent)allEvents[i].Event.Event).Data;
            Assert.Equal($"post-{i - 4}", data);
        }
    }

    private OpossumOptions CreateOptions()
    {
        var options = new OpossumOptions
        {
            RootPath = _tempPath,
            FlushEventsImmediately = false,
            WriteProtectEventFiles = false
        };
        options.UseStore("CrashRecoveryContext");
        return options;
    }
}
