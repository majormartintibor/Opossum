using Opossum;
using Opossum.Core;
using Opossum.DependencyInjection;
using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.DataSeeder.Core;
using Opossum.Samples.DataSeeder.Writers;

namespace Opossum.Samples.DataSeeder.IntegrationTests;

public sealed class DirectEventWriterTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _contextPath;

    public DirectEventWriterTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "OpossumSeederTests",
            Guid.NewGuid().ToString());
        _contextPath = Path.Combine(_tempRoot, "TestStore");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ── Event file creation ───────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_CreatesEventFiles_WithCorrectNamingAsync()
    {
        var events = CreateStudentEvents(3);
        var writer = new DirectEventWriter();

        await writer.WriteAsync(events, _contextPath);

        for (var i = 1; i <= 3; i++)
        {
            var filePath = Path.Combine(_contextPath, "events", $"{i:D10}.json");
            Assert.True(File.Exists(filePath), $"Expected event file not found: {filePath}");
        }
    }

    [Fact]
    public async Task WriteAsync_EventFiles_AreReadableByEventStoreAsync()
    {
        var studentId = Guid.NewGuid();
        var email = "test@example.com";
        var timestamp = DateTimeOffset.UtcNow.AddDays(-10);

        var events = new List<SequencedSeedEvent>
        {
            new(1,
                new DomainEvent
                {
                    Event = new StudentRegisteredEvent(studentId, "Test", "User", email),
                    Tags = [new Tag("studentId", studentId.ToString()), new Tag("studentEmail", email)]
                },
                new Metadata { Timestamp = timestamp })
        };

        await new DirectEventWriter().WriteAsync(events, _contextPath);

        // Verify round-trip via the public IEventStore API.
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddOpossum(options =>
        {
            options.RootPath = _tempRoot;
            options.UseStore("TestStore");
        });
        await using var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var readEvents = await eventStore.ReadAsync(Query.All(), null);

        Assert.Single(readEvents);
        Assert.Equal(1, readEvents[0].Position);
        var registeredEvent = Assert.IsType<StudentRegisteredEvent>(readEvents[0].Event.Event);
        Assert.Equal(studentId, registeredEvent.StudentId);
        Assert.Equal("Test", registeredEvent.FirstName);
        Assert.Equal("User", registeredEvent.LastName);
        Assert.Equal(email, registeredEvent.Email);
    }

    // ── Index file creation ───────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_CreatesEventTypeIndex_WithAllPositionsAsync()
    {
        var events = CreateStudentEvents(2);
        await new DirectEventWriter().WriteAsync(events, _contextPath);

        var indexPath = Path.Combine(
            _contextPath, "Indices", "EventType", "StudentRegisteredEvent.json");
        Assert.True(File.Exists(indexPath));

        var doc = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath));
        var positions = doc.RootElement
            .GetProperty("Positions")
            .EnumerateArray()
            .Select(p => p.GetInt64())
            .ToArray();
        Assert.Equal([1L, 2L], positions);
    }

    [Fact]
    public async Task WriteAsync_CreatesTagIndex_ForStudentIdAsync()
    {
        var studentId = Guid.NewGuid();
        var events = new List<SequencedSeedEvent>
        {
            new(1,
                new DomainEvent
                {
                    Event = new StudentRegisteredEvent(studentId, "A", "B", "a@b.com"),
                    Tags = [new Tag("studentId", studentId.ToString())]
                },
                new Metadata { Timestamp = DateTimeOffset.UtcNow })
        };

        await new DirectEventWriter().WriteAsync(events, _contextPath);

        var tagIndexPath = Path.Combine(
            _contextPath, "Indices", "Tags", $"studentId_{studentId}.json");
        Assert.True(File.Exists(tagIndexPath));
    }

    // ── Ledger ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_CreatesLedger_WithCorrectLastPositionAsync()
    {
        var events = CreateStudentEvents(5);
        await new DirectEventWriter().WriteAsync(events, _contextPath);

        var ledgerPath = Path.Combine(_contextPath, ".ledger");
        Assert.True(File.Exists(ledgerPath));

        var doc = JsonDocument.Parse(await File.ReadAllTextAsync(ledgerPath));
        Assert.Equal(5L, doc.RootElement.GetProperty("LastSequencePosition").GetInt64());
        Assert.Equal(5L, doc.RootElement.GetProperty("EventCount").GetInt64());
    }

    // ── Cross-process lock file ───────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_CreatesStoreLockFile_ForCrossProcessCoordinationAsync()
    {
        await new DirectEventWriter().WriteAsync(CreateStudentEvents(1), _contextPath);

        var lockFilePath = Path.Combine(_contextPath, ".store.lock");
        Assert.True(File.Exists(lockFilePath),
            ".store.lock must be created by DirectEventWriter so the seeded database is " +
            "structurally identical to one initialised through the normal IEventStore path.");
    }

    [Fact]
    public async Task WriteAsync_DoesNotOverwrite_ExistingStoreLockFileAsync()
    {
        // Pre-create a lock file with known content (simulates a file left by a prior process).
        var lockFilePath = Path.Combine(_contextPath, ".store.lock");
        Directory.CreateDirectory(_contextPath);
        await File.WriteAllTextAsync(lockFilePath, "sentinel");

        await new DirectEventWriter().WriteAsync(CreateStudentEvents(1), _contextPath);

        // Content must be unchanged — the writer must not truncate an existing lock file.
        var content = await File.ReadAllTextAsync(lockFilePath);
        Assert.Equal("sentinel", content);
    }

    // ── Append semantics ──────────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_AppendingTwoBatches_OffsetPositionsCorrectlyAsync()
    {
        var writer = new DirectEventWriter();

        await writer.WriteAsync(CreateStudentEvents(3), _contextPath);
        await writer.WriteAsync(CreateStudentEvents(2), _contextPath);

        // All 5 event files must exist.
        for (var i = 1; i <= 5; i++)
        {
            var filePath = Path.Combine(_contextPath, "events", $"{i:D10}.json");
            Assert.True(File.Exists(filePath), $"Missing event file for position {i}");
        }

        // Ledger must reflect the combined total.
        var doc = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_contextPath, ".ledger")));
        Assert.Equal(5L, doc.RootElement.GetProperty("LastSequencePosition").GetInt64());
        Assert.Equal(5L, doc.RootElement.GetProperty("EventCount").GetInt64());
    }

    [Fact]
    public async Task WriteAsync_AppendingTwoBatches_MergesEventTypeIndexAsync()
    {
        var writer = new DirectEventWriter();

        await writer.WriteAsync(CreateStudentEvents(2), _contextPath);
        await writer.WriteAsync(CreateStudentEvents(3), _contextPath);

        var indexPath = Path.Combine(
            _contextPath, "Indices", "EventType", "StudentRegisteredEvent.json");
        var doc = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath));
        var positions = doc.RootElement
            .GetProperty("Positions")
            .EnumerateArray()
            .Select(p => p.GetInt64())
            .ToArray();

        Assert.Equal([1L, 2L, 3L, 4L, 5L], positions);
    }

    // ── SeedPlan integration ──────────────────────────────────────────────────

    [Fact]
    public async Task SeedPlan_AssignsSequentialPositions_SortedByTimestampAsync()
    {
        // Arrange: generator produces events with descending timestamps.
        var generator = new ReversedTimestampGenerator();
        var plan = new SeedPlan([generator]);
        var writer = new DirectEventWriter();

        await plan.RunAsync(new SeedingConfiguration(), writer, _contextPath);

        // Assert: event files exist at positions 1..EventCount and their timestamps
        // correspond to the chronologically oldest event at position 1.
        for (var i = 1; i <= ReversedTimestampGenerator.EventCount; i++)
        {
            var filePath = Path.Combine(_contextPath, "events", $"{i:D10}.json");
            Assert.True(File.Exists(filePath));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<SequencedSeedEvent> CreateStudentEvents(int count)
    {
        var events = new List<SequencedSeedEvent>(count);
        for (var i = 1; i <= count; i++)
        {
            var studentId = Guid.NewGuid();
            events.Add(new SequencedSeedEvent(
                i,
                new DomainEvent
                {
                    Event = new StudentRegisteredEvent(
                        studentId, $"First{i}", $"Last{i}", $"student{i}@test.com"),
                    Tags =
                    [
                        new Tag("studentId", studentId.ToString()),
                        new Tag("studentEmail", $"student{i}@test.com")
                    ]
                },
                new Metadata { Timestamp = DateTimeOffset.UtcNow.AddDays(-i) }));
        }
        return events;
    }

    /// <summary>
    /// Minimal generator that produces events with descending timestamps to verify
    /// that <see cref="SeedPlan"/> sorts them into ascending chronological order.
    /// </summary>
    private sealed class ReversedTimestampGenerator : ISeedGenerator
    {
        public const int EventCount = 3;

        public IReadOnlyList<SeedEvent> Generate(SeedContext context, SeedingConfiguration config)
        {
            var events = new List<SeedEvent>();
            for (var i = EventCount; i >= 1; i--)
            {
                var studentId = Guid.NewGuid();
                events.Add(new SeedEvent(
                    new DomainEvent
                    {
                        Event = new StudentRegisteredEvent(
                            studentId, $"F{i}", $"L{i}", $"s{i}@test.com"),
                        Tags = [new Tag("studentId", studentId.ToString())]
                    },
                    new Metadata { Timestamp = DateTimeOffset.UtcNow.AddDays(-i) }));
            }
            return events;
        }
    }
}
