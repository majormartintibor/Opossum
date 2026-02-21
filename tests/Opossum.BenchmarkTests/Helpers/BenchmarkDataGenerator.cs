namespace Opossum.BenchmarkTests.Helpers;

/// <summary>
/// Generates test data for benchmarks
/// </summary>
public static class BenchmarkDataGenerator
{
    private static readonly Random _random = new(42); // Fixed seed for reproducibility

    /// <summary>
    /// Generates a collection of test events
    /// </summary>
    /// <param name="count">Number of events to generate</param>
    /// <param name="tagCount">Number of tags per event (0-10)</param>
    /// <param name="eventTypes">Event types to cycle through (null = use default)</param>
    public static List<NewEvent> GenerateEvents(
        int count,
        int tagCount = 2,
        string[]? eventTypes = null)
    {
        eventTypes ??= ["OrderCreated", "OrderShipped", "OrderDelivered", "OrderCancelled"];

        var events = new List<NewEvent>(count);

        for (int i = 0; i < count; i++)
        {
            var eventType = eventTypes[i % eventTypes.Length];
            var tags = GenerateTags(tagCount);

            events.Add(new NewEvent
            {
                Event = new DomainEvent
                {
                    EventType = eventType,
                    Event = new BenchmarkEvent
                    {
                        Id = Guid.NewGuid(),
                        Data = $"Event data {i}",
                        Timestamp = DateTimeOffset.UtcNow
                    },
                    Tags = tags
                },
                Metadata = new Metadata
                {
                    Timestamp = DateTimeOffset.UtcNow
                }
            });
        }

        return events;
    }

    /// <summary>
    /// Generates random tags
    /// </summary>
    public static List<Tag> GenerateTags(int count)
    {
        var tags = new List<Tag>(count);
        var tagKeys = new[] { "Region", "Environment", "Tenant", "UserId", "OrderId", "ProductId", "CategoryId", "Priority", "Status", "Version" };
        var tagValues = new[] { "US-West", "Production", "Tenant123", "User456", "Order789", "Product001", "Category-A", "High", "Active", "v1.0" };
        
        for (int i = 0; i < count && i < tagKeys.Length; i++)
        {
            tags.Add(new Tag
            {
                Key = tagKeys[i],
                Value = tagValues[_random.Next(tagValues.Length)]
            });
        }
        
        return tags;
    }

    /// <summary>
    /// Generates a query for single event type
    /// </summary>
    public static Query GenerateEventTypeQuery(string eventType)
    {
        return Query.FromEventTypes([eventType]);
    }

    /// <summary>
    /// Generates a query for multiple event types
    /// </summary>
    public static Query GenerateMultiEventTypeQuery(params string[] eventTypes)
    {
        return Query.FromEventTypes(eventTypes);
    }

    /// <summary>
    /// Generates a query for single tag
    /// </summary>
    public static Query GenerateTagQuery(string key, string value)
    {
        return Query.FromTags([new Tag { Key = key, Value = value }]);
    }

    /// <summary>
    /// Generates a query for multiple tags (AND logic)
    /// </summary>
    public static Query GenerateMultiTagQuery(params Tag[] tags)
    {
        return Query.FromTags(tags);
    }

    /// <summary>
    /// Creates a temporary directory for benchmark data
    /// </summary>
    public static string CreateTempDirectory(string prefix = "BenchmarkData")
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);
        return tempPath;
    }

    /// <summary>
    /// Gets a random subset of tags from generated events
    /// </summary>
    public static Tag[] GetRandomTags(int count, int maxTagsPerEvent = 2)
    {
        var events = GenerateEvents(100, maxTagsPerEvent);
        var allTags = events.SelectMany(e => e.Event.Tags).Distinct().ToList();
        
        return allTags
            .OrderBy(_ => _random.Next())
            .Take(Math.Min(count, allTags.Count))
            .ToArray();
    }
}

/// <summary>
/// Simple event for benchmarking (avoids complex serialization)
/// </summary>
public record BenchmarkEvent : IEvent
{
    public Guid Id { get; init; }
    public string Data { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
}
