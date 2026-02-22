using Opossum.Core;
using Opossum.Storage.FileSystem;

namespace Opossum.UnitTests.Storage.FileSystem;

public class JsonEventSerializerTests
{
    private readonly JsonEventSerializer _serializer;

    public JsonEventSerializerTests()
    {
        _serializer = new JsonEventSerializer();
    }

    // ========================================================================
    // Serialize Tests
    // ========================================================================

    [Fact]
    public void Serialize_WithValidEvent_ReturnsJsonString()
    {
        // Arrange
        var sequencedEvent = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "test" });

        // Act
        var json = _serializer.Serialize(sequencedEvent);

        // Assert
        Assert.NotNull(json);
        Assert.NotEmpty(json);
        Assert.Contains("position", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("event", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_WithNullEvent_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _serializer.Serialize(null!));
    }

    [Fact]
    public void Serialize_IncludesPosition()
    {
        // Arrange
        var sequencedEvent = CreateTestEvent(42, "TestEvent", new TestDomainEvent { Data = "test" });

        // Act
        var json = _serializer.Serialize(sequencedEvent);

        // Assert - Works with both minified and indented JSON
        Assert.Contains("\"position\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("42", json);
    }

    [Fact]
    public void Serialize_IncludesEventType()
    {
        // Arrange
        var sequencedEvent = CreateTestEvent(1, "StudentEnrolled", new TestDomainEvent { Data = "test" });

        // Act
        var json = _serializer.Serialize(sequencedEvent);

        // Assert
        Assert.Contains("StudentEnrolled", json);
    }

    [Fact]
    public void Serialize_IncludesTags()
    {
        // Arrange
        var sequencedEvent = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "test" });
        sequencedEvent.Event.Tags.Add(new Tag { Key = "courseId", Value = "123" });
        sequencedEvent.Event.Tags.Add(new Tag { Key = "studentId", Value = "456" });

        // Act
        var json = _serializer.Serialize(sequencedEvent);

        // Assert
        Assert.Contains("courseId", json);
        Assert.Contains("123", json);
        Assert.Contains("studentId", json);
        Assert.Contains("456", json);
    }

    [Fact]
    public void Serialize_IncludesMetadata()
    {
        // Arrange
        var correlationId = Guid.Parse("00000000-0000-0000-0000-000000000123");
        var sequencedEvent = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "test" });
        sequencedEvent.Metadata.Timestamp = DateTime.Parse("2024-12-01T12:00:00Z").ToUniversalTime();
        sequencedEvent.Metadata.CorrelationId = correlationId;

        // Act
        var json = _serializer.Serialize(sequencedEvent);

        // Assert
        Assert.Contains("metadata", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(correlationId.ToString(), json);
    }

    // ========================================================================
    // Deserialize Tests
    // ========================================================================

    [Fact]
    public void Deserialize_WithValidJson_ReturnsSequencedEvent()
    {
        // Arrange
        var original = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "test" });
        var json = _serializer.Serialize(original);

        // Act
        var deserialized = _serializer.Deserialize(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Position, deserialized.Position);
        Assert.Equal(original.Event.EventType, deserialized.Event.EventType);
    }

    [Fact]
    public void Deserialize_WithNullJson_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _serializer.Deserialize(null!));
    }

    [Fact]
    public void Deserialize_WithEmptyJson_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _serializer.Deserialize(""));
    }

    [Fact]
    public void Deserialize_WithWhitespaceJson_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _serializer.Deserialize("   "));
    }

    [Fact]
    public void Deserialize_WithInvalidJson_ThrowsJsonException()
    {
        // Arrange
        var invalidJson = "{ this is not valid JSON }";

        // Act & Assert
        Assert.Throws<JsonException>(() => _serializer.Deserialize(invalidJson));
    }

    [Fact]
    public void Deserialize_RestoresPosition()
    {
        // Arrange
        var original = CreateTestEvent(42, "TestEvent", new TestDomainEvent { Data = "test" });
        var json = _serializer.Serialize(original);

        // Act
        var deserialized = _serializer.Deserialize(json);

        // Assert
        Assert.Equal(42, deserialized.Position);
    }

    [Fact]
    public void Deserialize_RestoresEventType()
    {
        // Arrange
        var original = CreateTestEvent(1, "StudentEnrolled", new TestDomainEvent { Data = "test" });
        var json = _serializer.Serialize(original);

        // Act
        var deserialized = _serializer.Deserialize(json);

        // Assert
        Assert.Equal("StudentEnrolled", deserialized.Event.EventType);
    }

    [Fact]
    public void Deserialize_RestoresTags()
    {
        // Arrange
        var original = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "test" });
        original.Event.Tags.Add(new Tag { Key = "courseId", Value = "123" });
        original.Event.Tags.Add(new Tag { Key = "studentId", Value = "456" });
        var json = _serializer.Serialize(original);

        // Act
        var deserialized = _serializer.Deserialize(json);

        // Assert
        Assert.Equal(2, deserialized.Event.Tags.Count);
        Assert.Contains(deserialized.Event.Tags, t => t.Key == "courseId" && t.Value == "123");
        Assert.Contains(deserialized.Event.Tags, t => t.Key == "studentId" && t.Value == "456");
    }

    [Fact]
    public void Deserialize_RestoresMetadata()
    {
        // Arrange
        var correlationId = Guid.Parse("00000000-0000-0000-0000-000000000123");
        var original = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "test" });
        original.Metadata.CorrelationId = correlationId;
        var json = _serializer.Serialize(original);

        // Act
        var deserialized = _serializer.Deserialize(json);

        // Assert
        Assert.Equal(correlationId, deserialized.Metadata.CorrelationId);
    }

    // ========================================================================
    // Polymorphic Event Tests
    // ========================================================================

    [Fact]
    public void SerializeDeserialize_WithPolymorphicEvent_PreservesType()
    {
        // Arrange
        var testEvent = new TestDomainEvent { Data = "test data" };
        var sequencedEvent = CreateTestEvent(1, "TestEvent", testEvent);

        // Act
        var json = _serializer.Serialize(sequencedEvent);
        var deserialized = _serializer.Deserialize(json);

        // Assert
        Assert.IsType<TestDomainEvent>(deserialized.Event.Event);
        var deserializedEvent = (TestDomainEvent)deserialized.Event.Event;
        Assert.Equal("test data", deserializedEvent.Data);
    }

    [Fact]
    public void SerializeDeserialize_WithDifferentEventTypes_PreservesEachType()
    {
        // Arrange
        var event1 = CreateTestEvent(1, "Type1", new TestDomainEvent { Data = "data1" });
        var event2 = CreateTestEvent(2, "Type2", new AnotherTestEvent { Number = 42 });

        // Act
        var json1 = _serializer.Serialize(event1);
        var json2 = _serializer.Serialize(event2);
        var deserialized1 = _serializer.Deserialize(json1);
        var deserialized2 = _serializer.Deserialize(json2);

        // Assert
        Assert.IsType<TestDomainEvent>(deserialized1.Event.Event);
        Assert.IsType<AnotherTestEvent>(deserialized2.Event.Event);
        Assert.Equal("data1", ((TestDomainEvent)deserialized1.Event.Event).Data);
        Assert.Equal(42, ((AnotherTestEvent)deserialized2.Event.Event).Number);
    }

    // ========================================================================
    // Round-trip Tests
    // ========================================================================

    [Fact]
    public void RoundTrip_PreservesAllData()
    {
        // Arrange
        var original = CreateTestEvent(123, "CompleteEvent", new TestDomainEvent { Data = "complete" });
        original.Event.Tags.Add(new Tag { Key = "tag1", Value = "value1" });
        original.Event.Tags.Add(new Tag { Key = "tag2", Value = "value2" });
        original.Metadata.Timestamp = DateTime.Parse("2024-12-01T12:00:00Z").ToUniversalTime();
        original.Metadata.CorrelationId = Guid.Parse("00000000-0000-0000-0000-000000000456");

        // Act
        var json = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize(json);

        // Assert
        Assert.Equal(original.Position, deserialized.Position);
        Assert.Equal(original.Event.EventType, deserialized.Event.EventType);
        Assert.Equal(original.Event.Tags.Count, deserialized.Event.Tags.Count);
        Assert.Equal(original.Metadata.CorrelationId, deserialized.Metadata.CorrelationId);

        var deserializedEvent = (TestDomainEvent)deserialized.Event.Event;
        Assert.Equal("complete", deserializedEvent.Data);
    }

    // ========================================================================
    // Helper Methods
    // ========================================================================

    private static SequencedEvent CreateTestEvent(long position, string eventType, IEvent domainEvent)
    {
        return new SequencedEvent
        {
            Position = position,
            Event = new DomainEvent
            {
                EventType = eventType,
                Event = domainEvent,
                Tags = []
            },
            Metadata = new Metadata()
        };
    }
}

// Test event types
public record TestDomainEvent : IEvent
{
    public string Data { get; set; } = string.Empty;
}

public record AnotherTestEvent : IEvent
{
    public int Number { get; set; }
}
