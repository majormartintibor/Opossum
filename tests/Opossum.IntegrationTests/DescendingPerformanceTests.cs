using Opossum.Core;
using Opossum.Configuration;
using Opossum.Storage.FileSystem;

namespace Opossum.IntegrationTests;

public class DescendingPerformanceTests : IDisposable
{
    private readonly string _tempPath;
    private readonly OpossumOptions _options;
    private readonly FileSystemEventStore _store;

    public DescendingPerformanceTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"DescendingPerfTest_{Guid.NewGuid():N}");
        _options = new OpossumOptions
        {
            RootPath = _tempPath,
            FlushEventsImmediately = false
        };
        _options.UseStore("TestContext");

        _store = new FileSystemEventStore(_options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
        {
            try
            {
                Directory.Delete(_tempPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public async Task Descending_Order_Should_Be_Fast_With_Many_EventsAsync()
    {
        // Arrange - Create 500 events
        var events = new NewEvent[500];
        for (int i = 0; i < 500; i++)
        {
            events[i] = new NewEvent
            {
                Event = new DomainEvent
                {
                    EventType = "TestEvent",
                    Event = new TestEvent { Data = $"Data{i}" },
                    Tags = []
                },
                Metadata = new Metadata()
            };
        }

        await _store.AppendAsync(events, null);

        // Warm-up: read once to populate the OS page cache from real file reads.
        // Without this, the ascending measurement benefits from dirty write-cache while
        // the descending measurement may hit actual disk I/O, making the ratio non-deterministic.
        _ = await _store.ReadAsync(Query.All(), null);

        // Act - Measure ascending query (warm cache)
        var sw1 = System.Diagnostics.Stopwatch.StartNew();
        var ascending = await _store.ReadAsync(Query.All(), null);
        sw1.Stop();
        var ascendingTime = sw1.ElapsedMilliseconds;

        // Act - Measure descending query (warm cache)
        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        var descending = await _store.ReadAsync(Query.All(), [ReadOption.Descending]);
        sw2.Stop();
        var descendingTime = sw2.ElapsedMilliseconds;

        // Assert - Descending should be comparable to ascending (not 12x slower!).
        // Both reads start from a warm OS cache, so the ratio should be near 1x.
        // We allow up to 4x to absorb scheduler jitter and GC pauses that still occur
        // in a test environment. The original bug was ~12x; with the fix applied the
        // overhead is Array.Reverse(positions) which is microseconds, not milliseconds.
        var ratio = (double)descendingTime / Math.Max(ascendingTime, 1);

        Assert.True(ratio < 4.0,
            $"Descending took {descendingTime}ms vs Ascending {ascendingTime}ms (ratio: {ratio:F2}x). " +
            $"Expected <4x overhead with the fix applied. Original bug was ~12x.");

        // Also verify correctness
        Assert.Equal(500, descending.Length);
        Assert.Equal(500, descending[0].Position); // Newest first
        Assert.Equal(1, descending[499].Position); // Oldest last
    }

    private class TestEvent : IEvent
    {
        public string Data { get; init; } = "";
    }
}
