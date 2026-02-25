using Opossum.Configuration;
using Opossum.Core;
using Opossum.Exceptions;
using Opossum.Extensions;
using Opossum.IntegrationTests.Helpers;
using Opossum.Storage.FileSystem;

namespace Opossum.IntegrationTests;

/// <summary>
/// Integration tests that verify the cross-process append safety introduced in ADR-005.
///
/// Key insight: Windows enforces FileShare.None at the file-handle level, not the process
/// level. Two FileSystemEventStore instances in the same test process sharing the same
/// directory genuinely compete for the .store.lock file, giving us real lock-contention
/// behaviour without spawning child processes.
///
/// Each test uses its own isolated temp directory, so tests can run in parallel.
/// </summary>
public sealed class CrossProcessAppendSafetyTests : IDisposable
{
    private readonly string _tempPath;
    private readonly FileSystemEventStore _store1;
    private readonly FileSystemEventStore _store2;

    private sealed record CrossProcessTestEvent(string Data) : IEvent;

    public CrossProcessAppendSafetyTests()
    {
        _tempPath = Path.Combine(
            Path.GetTempPath(),
            "OpossumCrossProcessTests",
            Guid.NewGuid().ToString("N"));

        var options1 = new OpossumOptions
        {
            RootPath = _tempPath,
            FlushEventsImmediately = false
        };
        options1.UseStore("TestContext");
        _store1 = new FileSystemEventStore(options1);

        var options2 = new OpossumOptions
        {
            RootPath = _tempPath,
            FlushEventsImmediately = false
        };
        options2.UseStore("TestContext");
        _store2 = new FileSystemEventStore(options2);
    }

    public void Dispose()
    {
        _store1.Dispose();
        _store2.Dispose();
        TestDirectoryHelper.ForceDelete(_tempPath);
    }

    // ========================================================================
    // Contiguous positions — two competing instances
    // ========================================================================

    [Fact]
    public async Task TwoInstances_ConcurrentAppends_ProduceContiguousPositions()
    {
        const int appendsPerInstance = 50;
        const int totalAppends = appendsPerInstance * 2;

        var tasks = Enumerable.Range(0, appendsPerInstance)
            .Select(i => Task.Run(async () =>
            {
                var evt = new CrossProcessTestEvent($"Instance1-{i}")
                    .ToDomainEvent()
                    .WithTag("source", "instance1")
                    .Build();
                await _store1.AppendAsync([evt], null);
            }))
            .Concat(Enumerable.Range(0, appendsPerInstance)
                .Select(i => Task.Run(async () =>
                {
                    var evt = new CrossProcessTestEvent($"Instance2-{i}")
                        .ToDomainEvent()
                        .WithTag("source", "instance2")
                        .Build();
                    await _store2.AppendAsync([evt], null);
                })))
            .ToArray();

        await Task.WhenAll(tasks);

        // Both instances share storage — read from either one
        var allEvents = await _store1.ReadAsync(Query.All(), null);

        Assert.Equal(totalAppends, allEvents.Length);

        // Positions must be contiguous 1..100 with no gaps and no duplicates
        var positions = allEvents.Select(e => e.Position).OrderBy(p => p).ToArray();
        Assert.Equal(Enumerable.Range(1, totalAppends).Select(i => (long)i), positions);
        Assert.Equal(positions.Length, positions.Distinct().Count());
    }

    // ========================================================================
    // No event overwrite — every payload must survive
    // ========================================================================

    [Fact]
    public async Task TwoInstances_ConcurrentAppends_NoEventOverwrite()
    {
        const int appendsPerInstance = 50;

        var tasks = Enumerable.Range(0, appendsPerInstance)
            .Select(i => Task.Run(async () =>
            {
                var evt = new CrossProcessTestEvent($"Store1-Payload-{i}")
                    .ToDomainEvent()
                    .WithTag("source", "store1")
                    .Build();
                await _store1.AppendAsync([evt], null);
            }))
            .Concat(Enumerable.Range(0, appendsPerInstance)
                .Select(i => Task.Run(async () =>
                {
                    var evt = new CrossProcessTestEvent($"Store2-Payload-{i}")
                        .ToDomainEvent()
                        .WithTag("source", "store2")
                        .Build();
                    await _store2.AppendAsync([evt], null);
                })))
            .ToArray();

        await Task.WhenAll(tasks);

        var store1Events = await _store1.ReadAsync(
            Query.FromTags(new Tag("source", "store1")), null);
        var store2Events = await _store1.ReadAsync(
            Query.FromTags(new Tag("source", "store2")), null);

        // Every event payload from both instances must be recoverable
        Assert.Equal(appendsPerInstance, store1Events.Length);
        Assert.Equal(appendsPerInstance, store2Events.Length);
    }

    // ========================================================================
    // DCB guarantee across instances
    // ========================================================================

    [Fact]
    public async Task TwoInstances_AppendCondition_ExactlyOneSucceeds()
    {
        // Both instances read an empty store (position 0) and decide to append.
        // FailIfEventsMatch = Query.FromItems() (empty) means:
        // "fail if any events at all have been appended since AfterSequencePosition."
        var condition = new AppendCondition
        {
            AfterSequencePosition = 0,
            FailIfEventsMatch = Query.FromItems()
        };

        var successCount = 0;
        var failureCount = 0;

        // Use a gate to maximise the chance of genuine concurrency
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var task1 = Task.Run(async () =>
        {
            await startGate.Task;
            try
            {
                var evt = new CrossProcessTestEvent("Instance1-Decision")
                    .ToDomainEvent().Build();
                await _store1.AppendAsync([evt], condition);
                Interlocked.Increment(ref successCount);
            }
            catch (AppendConditionFailedException)
            {
                Interlocked.Increment(ref failureCount);
            }
        });

        var task2 = Task.Run(async () =>
        {
            await startGate.Task;
            try
            {
                var evt = new CrossProcessTestEvent("Instance2-Decision")
                    .ToDomainEvent().Build();
                await _store2.AppendAsync([evt], condition);
                Interlocked.Increment(ref successCount);
            }
            catch (AppendConditionFailedException)
            {
                Interlocked.Increment(ref failureCount);
            }
        });

        // Release both tasks simultaneously
        startGate.SetResult();
        await Task.WhenAll(task1, task2);

        Assert.Equal(1, successCount);
        Assert.Equal(1, failureCount);

        // Exactly one event should have been written
        var allEvents = await _store1.ReadAsync(Query.All(), null);
        Assert.Single(allEvents);
    }

    // ========================================================================
    // Lock timeout
    // ========================================================================

    [Fact]
    public async Task LockTimeout_WhenLockHeldExternally_ThrowsTimeoutException()
    {
        var options = new OpossumOptions
        {
            RootPath = _tempPath,
            FlushEventsImmediately = false,
            CrossProcessLockTimeout = TimeSpan.FromMilliseconds(200)
        };
        options.UseStore("TestContext");
        using var timedOutStore = new FileSystemEventStore(options);

        // Ensure the context directory exists before opening the lock file
        var contextPath = Path.Combine(_tempPath, "TestContext");
        Directory.CreateDirectory(contextPath);

        // Hold the lock file exclusively from outside the store
        using var blocker = new FileStream(
            Path.Combine(contextPath, ".store.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1,
            FileOptions.None);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            timedOutStore.AppendAsync(
                [new CrossProcessTestEvent("ShouldNotAppend").ToDomainEvent().Build()],
                null));
        sw.Stop();

        // Exception message must identify the lock file
        Assert.Contains(".store.lock", ex.Message);

        // Must have waited approximately the configured timeout
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(150),
            $"Timed out too quickly: {sw.Elapsed.TotalMilliseconds:F0}ms (expected >= 150ms)");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2),
            $"Timed out too slowly: {sw.Elapsed.TotalMilliseconds:F0}ms (expected < 2000ms)");
    }

    // ========================================================================
    // Performance sanity check
    // ========================================================================

    [Fact]
    public async Task SingleInstance_SequentialAppends_CompletesWithinReasonableTime()
    {
        const int eventCount = 100;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < eventCount; i++)
        {
            var evt = new CrossProcessTestEvent($"PerfEvent-{i}").ToDomainEvent().Build();
            await _store1.AppendAsync([evt], null);
        }
        sw.Stop();

        var allEvents = await _store1.ReadAsync(Query.All(), null);
        Assert.Equal(eventCount, allEvents.Length);

        // 100 sequential appends with FlushEventsImmediately=false should complete well
        // under 5 seconds even on slow CI machines. This catches catastrophic regressions
        // caused by the cross-process lock (e.g. if every append were waiting for timeout).
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"100 sequential appends took {sw.Elapsed.TotalMilliseconds:F0}ms. " +
            $"Expected < 5000ms. The cross-process lock may be adding unexpected latency.");
    }
}
