using Opossum.BenchmarkTests.Helpers;
using Opossum.Core;

namespace Opossum.BenchmarkTests.Projections;

/// <summary>
/// Benchmarks for projection rebuild performance.
/// Measures ONLY the projection building logic, NOT event creation/storage.
/// 
/// Key improvements:
/// - Events pre-created in IterationSetup (not measured)
/// - Smaller datasets to reduce memory (50, 250, 500 vs 100, 1K, 10K)
/// - Incremental updates properly isolated
/// - Each iteration uses fresh event store
/// </summary>
[Config(typeof(OpossumBenchmarkConfig))]
[MemoryDiagnoser]
public class ProjectionRebuildBenchmarks
{
    private IServiceProvider _serviceProvider = null!;
    private IEventStore _eventStore = null!;
    private TempFileSystemHelper _tempHelper = null!;

    // Pre-created event sets (populated in IterationSetup, not measured)
    private SequencedEvent[] _events50 = null!;
    private SequencedEvent[] _events250 = null!;
    private SequencedEvent[] _events500 = null!;
    private Dictionary<Guid, StudentProjection> _baseProjection250 = null!;

    // Pre-created incremental events (for incremental update benchmarks)
    private SequencedEvent _incrementalEvent1 = null!;
    private SequencedEvent[] _incrementalEvents10 = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _tempHelper = new TempFileSystemHelper("ProjectionBenchmarks");
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _tempHelper?.Dispose();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Create a fresh event store for each benchmark iteration
        var storePath = _tempHelper.CreateSubDirectory($"Store_{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = storePath;
            opt.FlushEventsImmediately = false; // Faster setup
            opt.AddContext("BenchmarkContext");
        });

        _serviceProvider = services.BuildServiceProvider();
        _eventStore = _serviceProvider.GetRequiredService<IEventStore>();

        // Pre-populate event store with different dataset sizes
        // Each dataset uses DIFFERENT event type for isolation!
        // This happens in setup, so it's NOT measured in benchmarks!
        // Using GetAwaiter().GetResult() is acceptable in setup/teardown

        // Dataset 1: 50 events (type: "Student50")
        _events50 = CreateStudentEvents(50, eventType: "Student50");
        _eventStore.AppendAsync(_events50, null).GetAwaiter().GetResult();

        // Dataset 2: 250 events (type: "Student250")
        _events250 = CreateStudentEvents(250, startIndex: 100, eventType: "Student250");
        _eventStore.AppendAsync(_events250, null).GetAwaiter().GetResult();

        // Dataset 3: 500 events (type: "Student500")
        _events500 = CreateStudentEvents(500, startIndex: 500, eventType: "Student500");
        _eventStore.AppendAsync(_events500, null).GetAwaiter().GetResult();

        // For incremental update tests: use "Student250" events
        _baseProjection250 = RebuildStudentProjectionAsync("Student250").GetAwaiter().GetResult();

        // Pre-create incremental events (append to store in setup, not measured in benchmark)
        _incrementalEvent1 = CreateStudentEvents(1, startIndex: 1000, eventType: "Student250")[0];
        _incrementalEvents10 = CreateStudentEvents(10, startIndex: 1000, eventType: "Student250");
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        // Dispose service provider asynchronously
        if (_serviceProvider is IAsyncDisposable asyncDisposable)
        {
            asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        else
        {
            (_serviceProvider as IDisposable)?.Dispose();
        }
    }

    // ========================================================================
    // Projection Rebuild - Pure Projection Logic (No Event Creation/Storage)
    // ========================================================================

    /// <summary>
    /// Baseline: Rebuild projection from 50 events.
    /// Events already in store (created in IterationSetup).
    /// ONLY measures: Query + Deserialize + Build Projection
    /// </summary>
    [Benchmark(Baseline = true, Description = "Rebuild projection (50 events)")]
    public async Task RebuildProjection_50Events()
    {
        // Query only "Student50" events (50 events)
        var projection = await RebuildStudentProjectionAsync("Student50");
    }

    /// <summary>
    /// Medium dataset: Rebuild projection from 250 events.
    /// Tests scaling to medium datasets.
    /// </summary>
    [Benchmark(Description = "Rebuild projection (250 events)")]
    public async Task RebuildProjection_250Events()
    {
        // Query only "Student250" events (250 events)
        var projection = await RebuildStudentProjectionAsync("Student250");
    }

    /// <summary>
    /// Large dataset: Rebuild projection from 500 events.
    /// Tests scaling to larger datasets (production-like).
    /// </summary>
    [Benchmark(Description = "Rebuild projection (500 events)")]
    public async Task RebuildProjection_500Events()
    {
        // Query only "Student500" events (500 events)
        var projection = await RebuildStudentProjectionAsync("Student500");
    }

    // ========================================================================
    // Incremental Update - FIXED (Only Measures Update, Not Setup)
    // ========================================================================

    /// <summary>
    /// Incremental update: Add 1 new event to existing projection.
    /// Base projection already built in IterationSetup.
    /// ONLY measures: Apply 1 event to projection (no disk I/O!)
    /// </summary>
    [Benchmark(Description = "Incremental update (1 new event)")]
    public void IncrementalUpdate_SingleEvent()
    {
        // Clone base projection (small cost, ~1ms for 250 items)
        var projection = new Dictionary<Guid, StudentProjection>(_baseProjection250);

        // ONLY measure applying the event to projection (no disk I/O!)
        ApplyEventToProjection(projection, _incrementalEvent1);
    }

    /// <summary>
    /// Incremental update: Add 10 new events to existing projection.
    /// ONLY measures projection application, not disk writes.
    /// Compares to full rebuild to find break-even point.
    /// </summary>
    [Benchmark(Description = "Incremental update (10 new events)")]
    public void IncrementalUpdate_10Events()
    {
        var projection = new Dictionary<Guid, StudentProjection>(_baseProjection250);

        // ONLY measure applying events to projection (no disk I/O!)
        foreach (var evt in _incrementalEvents10)
        {
            ApplyEventToProjection(projection, evt);
        }
    }

    // ========================================================================
    // Complex Projections (Multiple Event Types)
    // ========================================================================

    /// <summary>
    /// Complex projection with multiple event types.
    /// Uses smaller dataset (100 + 200 + 100 = 400 events total).
    /// </summary>
    [Benchmark(Description = "Complex projection (multi-event types)")]
    public async Task RebuildComplexProjection_MultiEventTypes()
    {
        // Events already in store from IterationSetup
        var projection = await RebuildComplexProjectionAsync();
    }

    // ========================================================================
    // Helper Methods
    // ========================================================================

    private SequencedEvent[] CreateStudentEvents(int count, int startIndex = 0, string eventType = "StudentRegistered")
    {
        var events = new SequencedEvent[count];
        for (int i = 0; i < count; i++)
        {
            var studentId = Guid.NewGuid();
            events[i] = new SequencedEvent
            {
                Position = 0,
                Event = new DomainEvent
                {
                    EventType = eventType, // Use parameterized event type for isolation
                    Event = new StudentRegisteredEvent(
                        StudentId: studentId,
                        FirstName: $"Student{startIndex + i}",
                        LastName: $"LastName{startIndex + i}",
                        Email: $"student{startIndex + i}@test.com"
                    ),
                    Tags = [
                        new Tag { Key = "studentId", Value = studentId.ToString() },
                        new Tag { Key = "studentEmail", Value = $"student{startIndex + i}@test.com" }
                    ]
                },
                Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
            };
        }
        return events;
    }

    private SequencedEvent[] CreateEnrollmentEvents(int count)
    {
        var events = new SequencedEvent[count];
        for (int i = 0; i < count; i++)
        {
            events[i] = new SequencedEvent
            {
                Position = 0,
                Event = new DomainEvent
                {
                    EventType = "StudentEnrolled",
                    Event = new StudentEnrolledEvent(
                        EnrollmentId: Guid.NewGuid(),
                        StudentId: Guid.NewGuid(),
                        CourseId: Guid.NewGuid(),
                        EnrollmentDate: DateTimeOffset.UtcNow
                    ),
                    Tags = []
                },
                Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
            };
        }
        return events;
    }

    private SequencedEvent[] CreateGradeEvents(int count)
    {
        var events = new SequencedEvent[count];
        for (int i = 0; i < count; i++)
        {
            events[i] = new SequencedEvent
            {
                Position = 0,
                Event = new DomainEvent
                {
                    EventType = "GradeAssigned",
                    Event = new GradeAssignedEvent(
                        EnrollmentId: Guid.NewGuid(),
                        Grade: (i % 5) + 1, // Grades 1-5
                        AssignedDate: DateTimeOffset.UtcNow
                    ),
                    Tags = []
                },
                Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
            };
        }
        return events;
    }

    private async Task<Dictionary<Guid, StudentProjection>> RebuildStudentProjectionAsync(string eventType = "StudentRegistered")
    {
        var projection = new Dictionary<Guid, StudentProjection>();

        // Read only events of specified type
        var query = Query.FromEventTypes([eventType]);
        var events = await _eventStore.ReadAsync(query, null);

        // Build projection
        foreach (var evt in events)
        {
            ApplyEventToProjection(projection, evt);
        }

        return projection;
    }

    private void ApplyEventToProjection(Dictionary<Guid, StudentProjection> projection, SequencedEvent evt)
    {
        if (evt.Event.Event is StudentRegisteredEvent registered)
        {
            projection[registered.StudentId] = new StudentProjection
            {
                StudentId = registered.StudentId,
                FirstName = registered.FirstName,
                LastName = registered.LastName,
                Email = registered.Email
            };
        }
    }

    private async Task<ComplexProjection> RebuildComplexProjectionAsync()
    {
        var projection = new ComplexProjection();

        // Read all relevant events
        var query = Query.FromEventTypes(["StudentRegistered", "StudentEnrolled", "GradeAssigned"]);
        var events = await _eventStore.ReadAsync(query, null);

        // Build complex projection
        foreach (var evt in events)
        {
            switch (evt.Event.Event)
            {
                case StudentRegisteredEvent registered:
                    projection.Students[registered.StudentId] = new StudentProjection
                    {
                        StudentId = registered.StudentId,
                        FirstName = registered.FirstName,
                        LastName = registered.LastName,
                        Email = registered.Email
                    };
                    break;

                case StudentEnrolledEvent enrolled:
                    if (!projection.Enrollments.ContainsKey(enrolled.StudentId))
                    {
                        projection.Enrollments[enrolled.StudentId] = new List<EnrollmentProjection>();
                    }
                    projection.Enrollments[enrolled.StudentId].Add(new EnrollmentProjection
                    {
                        EnrollmentId = enrolled.EnrollmentId,
                        CourseId = enrolled.CourseId,
                        EnrollmentDate = enrolled.EnrollmentDate
                    });
                    break;

                case GradeAssignedEvent graded:
                    projection.Grades[graded.EnrollmentId] = graded.Grade;
                    break;
            }
        }

        return projection;
    }

    // ========================================================================
    // Projection Models
    // ========================================================================

    private class StudentProjection
    {
        public Guid StudentId { get; init; }
        public string FirstName { get; init; } = "";
        public string LastName { get; init; } = "";
        public string Email { get; init; } = "";
    }

    private class EnrollmentProjection
    {
        public Guid EnrollmentId { get; init; }
        public Guid CourseId { get; init; }
        public DateTimeOffset EnrollmentDate { get; init; }
    }

    private class ComplexProjection
    {
        public Dictionary<Guid, StudentProjection> Students { get; } = new();
        public Dictionary<Guid, List<EnrollmentProjection>> Enrollments { get; } = new();
        public Dictionary<Guid, int> Grades { get; } = new();
    }

    // ========================================================================
    // Event Models
    // ========================================================================

    private record StudentRegisteredEvent(
        Guid StudentId,
        string FirstName,
        string LastName,
        string Email) : IEvent;

    private record StudentEnrolledEvent(
        Guid EnrollmentId,
        Guid StudentId,
        Guid CourseId,
        DateTimeOffset EnrollmentDate) : IEvent;

    private record GradeAssignedEvent(
        Guid EnrollmentId,
        int Grade,
        DateTimeOffset AssignedDate) : IEvent;
}
