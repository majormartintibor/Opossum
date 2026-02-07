namespace Opossum.BenchmarkTests.Helpers;

/// <summary>
/// Factory for creating events of various sizes for benchmarking
/// </summary>
public static class EventFactory
{
    /// <summary>
    /// Creates a small event (~100 bytes)
    /// </summary>
    public static SequencedEvent CreateSmallEvent(long position = 0)
    {
        return new SequencedEvent
        {
            Position = position,
            Event = new DomainEvent
            {
                EventType = "SmallEvent",
                Event = new SmallBenchmarkEvent
                {
                    Id = Guid.NewGuid(),
                    Value = 42
                },
                Tags = []
            },
            Metadata = new Metadata
            {
                Timestamp = DateTimeOffset.UtcNow
            }
        };
    }

    /// <summary>
    /// Creates a medium event (~1KB)
    /// </summary>
    public static SequencedEvent CreateMediumEvent(long position = 0)
    {
        return new SequencedEvent
        {
            Position = position,
            Event = new DomainEvent
            {
                EventType = "MediumEvent",
                Event = new MediumBenchmarkEvent
                {
                    Id = Guid.NewGuid(),
                    Data = new string('X', 900), // ~900 bytes
                    Values = Enumerable.Range(1, 10).ToArray()
                },
                Tags = BenchmarkDataGenerator.GenerateTags(3)
            },
            Metadata = new Metadata
            {
                Timestamp = DateTimeOffset.UtcNow
            }
        };
    }

    /// <summary>
    /// Creates a large event (~10KB)
    /// </summary>
    public static SequencedEvent CreateLargeEvent(long position = 0)
    {
        return new SequencedEvent
        {
            Position = position,
            Event = new DomainEvent
            {
                EventType = "LargeEvent",
                Event = new LargeBenchmarkEvent
                {
                    Id = Guid.NewGuid(),
                    LargeData = new string('Y', 9500), // ~9.5KB
                    Items = Enumerable.Range(1, 50)
                        .Select(i => new DataItem { Id = i, Name = $"Item_{i}", Value = i * 100 })
                        .ToList()
                },
                Tags = BenchmarkDataGenerator.GenerateTags(5)
            },
            Metadata = new Metadata
            {
                Timestamp = DateTimeOffset.UtcNow
            }
        };
    }
}

/// <summary>
/// Small event payload (~100 bytes when serialized)
/// </summary>
public record SmallBenchmarkEvent : IEvent
{
    public Guid Id { get; init; }
    public int Value { get; init; }
}

/// <summary>
/// Medium event payload (~1KB when serialized)
/// </summary>
public record MediumBenchmarkEvent : IEvent
{
    public Guid Id { get; init; }
    public string Data { get; init; } = string.Empty;
    public int[] Values { get; init; } = [];
}

/// <summary>
/// Large event payload (~10KB when serialized)
/// </summary>
public record LargeBenchmarkEvent : IEvent
{
    public Guid Id { get; init; }
    public string LargeData { get; init; } = string.Empty;
    public List<DataItem> Items { get; init; } = [];
}

public record DataItem
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal Value { get; init; }
}
