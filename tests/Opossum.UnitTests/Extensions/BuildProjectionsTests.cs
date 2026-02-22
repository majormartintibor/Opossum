using Opossum.Core;
using Opossum.Extensions;

namespace Opossum.UnitTests.Extensions;

/// <summary>
/// Unit tests for BuildProjections extension method
/// </summary>
public class BuildProjectionsTests
{
    #region Test Data Classes

    private record StudentProjection(Guid StudentId, string Name, string Email, int CourseCount)
    {
        public static StudentProjection Apply(IEvent evt, StudentProjection? current)
        {
            return evt switch
            {
                StudentCreatedEvent created => new StudentProjection(
                    created.StudentId,
                    created.Name,
                    created.Email,
                    0),

                StudentEnrolledEvent when current != null =>
                    current with { CourseCount = current.CourseCount + 1 },

                StudentNameChangedEvent nameChanged when current != null =>
                    current with { Name = nameChanged.NewName },

                _ => current!
            };
        }
    }

    private record StudentCreatedEvent(Guid StudentId, string Name, string Email) : IEvent;
    private record StudentEnrolledEvent(Guid StudentId, Guid CourseId) : IEvent;
    private record StudentNameChangedEvent(Guid StudentId, string NewName) : IEvent;

    #endregion

    #region BuildProjections Basic Tests

    [Fact]
    public void BuildProjections_WithSingleAggregate_BuildsOneProjection()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var events = new[]
        {
            CreateSequencedEvent(1, new StudentCreatedEvent(studentId, "Alice", "alice@test.com"), studentId),
            CreateSequencedEvent(2, new StudentEnrolledEvent(studentId, Guid.NewGuid()), studentId)
        };

        // Act
        var projections = events.BuildProjections<StudentProjection>(
            keySelector: e => e.Event.Tags.First(t => t.Key == "studentId").Value,
            applyEvent: StudentProjection.Apply
        ).ToList();

        // Assert
        Assert.Single(projections);
        Assert.Equal(studentId, projections[0].StudentId);
        Assert.Equal("Alice", projections[0].Name);
        Assert.Equal(1, projections[0].CourseCount);
    }

    [Fact]
    public void BuildProjections_WithMultipleEntities_BuildsMultipleProjections()
    {
        // Arrange
        var student1Id = Guid.NewGuid();
        var student2Id = Guid.NewGuid();
        var events = new[]
        {
            CreateSequencedEvent(1, new StudentCreatedEvent(student1Id, "Alice", "alice@test.com"), student1Id),
            CreateSequencedEvent(2, new StudentCreatedEvent(student2Id, "Bob", "bob@test.com"), student2Id),
            CreateSequencedEvent(3, new StudentEnrolledEvent(student1Id, Guid.NewGuid()), student1Id),
            CreateSequencedEvent(4, new StudentEnrolledEvent(student2Id, Guid.NewGuid()), student2Id),
            CreateSequencedEvent(5, new StudentEnrolledEvent(student2Id, Guid.NewGuid()), student2Id)
        };

        // Act
        var projections = events.BuildProjections<StudentProjection>(
            keySelector: e => e.Event.Tags.First(t => t.Key == "studentId").Value,
            applyEvent: StudentProjection.Apply
        ).ToList();

        // Assert
        Assert.Equal(2, projections.Count);

        var alice = projections.First(p => p.StudentId == student1Id);
        Assert.Equal("Alice", alice.Name);
        Assert.Equal(1, alice.CourseCount);

        var bob = projections.First(p => p.StudentId == student2Id);
        Assert.Equal("Bob", bob.Name);
        Assert.Equal(2, bob.CourseCount);
    }

    [Fact]
    public void BuildProjections_AppliesEventsInSequence()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var events = new[]
        {
            CreateSequencedEvent(1, new StudentCreatedEvent(studentId, "Alice", "alice@test.com"), studentId),
            CreateSequencedEvent(2, new StudentNameChangedEvent(studentId, "Alice Smith"), studentId),
            CreateSequencedEvent(3, new StudentEnrolledEvent(studentId, Guid.NewGuid()), studentId),
            CreateSequencedEvent(4, new StudentEnrolledEvent(studentId, Guid.NewGuid()), studentId)
        };

        // Act
        var projections = events.BuildProjections<StudentProjection>(
            keySelector: e => e.Event.Tags.First(t => t.Key == "studentId").Value,
            applyEvent: StudentProjection.Apply
        ).ToList();

        // Assert
        Assert.Single(projections);
        Assert.Equal("Alice Smith", projections[0].Name); // Name was changed
        Assert.Equal(2, projections[0].CourseCount); // Two enrollments
    }

    [Fact]
    public void BuildProjections_WithEmptyArray_ReturnsEmpty()
    {
        // Arrange
        var events = Array.Empty<SequencedEvent>();

        // Act
        var projections = events.BuildProjections<StudentProjection>(
            keySelector: e => e.Event.Tags.First(t => t.Key == "studentId").Value,
            applyEvent: StudentProjection.Apply
        ).ToList();

        // Assert
        Assert.Empty(projections);
    }

    [Fact]
    public void BuildProjections_HandlesNullSeedState()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var events = new[]
        {
            CreateSequencedEvent(1, new StudentCreatedEvent(studentId, "Alice", "alice@test.com"), studentId)
        };

        // Act
        var projections = events.BuildProjections<StudentProjection>(
            keySelector: e => e.Event.Tags.First(t => t.Key == "studentId").Value,
            applyEvent: (evt, current) =>
            {
                // Verify current is null on first event
                Assert.Null(current);
                return StudentProjection.Apply(evt, current);
            }
        ).ToList();

        // Assert
        Assert.Single(projections);
    }

    #endregion

    #region BuildProjections Edge Cases

    [Fact]
    public void BuildProjections_ThrowsIfEventsIsNull()
    {
        // Arrange
        SequencedEvent[]? nullEvents = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            nullEvents!.BuildProjections<StudentProjection>(
                keySelector: e => "test",
                applyEvent: StudentProjection.Apply
            )
        );
    }

    [Fact]
    public void BuildProjections_ThrowsIfKeySelectorIsNull()
    {
        // Arrange
        var events = Array.Empty<SequencedEvent>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            events.BuildProjections<StudentProjection>(
                keySelector: null!,
                applyEvent: StudentProjection.Apply
            )
        );
    }

    [Fact]
    public void BuildProjections_ThrowsIfApplyEventIsNull()
    {
        // Arrange
        var events = Array.Empty<SequencedEvent>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            events.BuildProjections<StudentProjection>(
                keySelector: e => "test",
                applyEvent: null!
            )
        );
    }

    [Fact]
    public void BuildProjections_FiltersOutNullProjections()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var events = new[]
        {
            CreateSequencedEvent(1, new StudentCreatedEvent(studentId, "Alice", "alice@test.com"), studentId)
        };

        // Act - applyEvent returns null
        var projections = events.BuildProjections<StudentProjection>(
            keySelector: e => e.Event.Tags.First(t => t.Key == "studentId").Value,
            applyEvent: (evt, current) => null // Always return null
        ).ToList();

        // Assert
        Assert.Empty(projections); // Null projections are filtered out
    }

    [Fact]
    public void BuildProjections_WithInterleavedEvents_GroupsCorrectly()
    {
        // Arrange - Events for two students are interleaved
        var student1Id = Guid.NewGuid();
        var student2Id = Guid.NewGuid();
        var events = new[]
        {
            CreateSequencedEvent(1, new StudentCreatedEvent(student1Id, "Alice", "alice@test.com"), student1Id),
            CreateSequencedEvent(2, new StudentCreatedEvent(student2Id, "Bob", "bob@test.com"), student2Id),
            CreateSequencedEvent(3, new StudentEnrolledEvent(student1Id, Guid.NewGuid()), student1Id),
            CreateSequencedEvent(4, new StudentEnrolledEvent(student2Id, Guid.NewGuid()), student2Id),
            CreateSequencedEvent(5, new StudentNameChangedEvent(student1Id, "Alice Smith"), student1Id)
        };

        // Act
        var projections = events.BuildProjections<StudentProjection>(
            keySelector: e => e.Event.Tags.First(t => t.Key == "studentId").Value,
            applyEvent: StudentProjection.Apply
        ).ToList();

        // Assert
        Assert.Equal(2, projections.Count);
        Assert.Equal("Alice Smith", projections.First(p => p.StudentId == student1Id).Name);
        Assert.Equal("Bob", projections.First(p => p.StudentId == student2Id).Name);
    }

    #endregion

    #region Integration Scenarios

    [Fact]
    public void BuildProjections_RealWorldScenario_StudentEnrollments()
    {
        // Arrange - Create 3 students with various activities
        var student1 = Guid.NewGuid();
        var student2 = Guid.NewGuid();
        var student3 = Guid.NewGuid();

        var events = new[]
        {
            // Student 1: Created, enrolled in 2 courses
            CreateSequencedEvent(1, new StudentCreatedEvent(student1, "Alice", "alice@test.com"), student1),
            CreateSequencedEvent(2, new StudentEnrolledEvent(student1, Guid.NewGuid()), student1),
            CreateSequencedEvent(3, new StudentEnrolledEvent(student1, Guid.NewGuid()), student1),
            
            // Student 2: Created, name changed, enrolled in 1 course
            CreateSequencedEvent(4, new StudentCreatedEvent(student2, "Bob", "bob@test.com"), student2),
            CreateSequencedEvent(5, new StudentNameChangedEvent(student2, "Robert"), student2),
            CreateSequencedEvent(6, new StudentEnrolledEvent(student2, Guid.NewGuid()), student2),
            
            // Student 3: Created, no other activities
            CreateSequencedEvent(7, new StudentCreatedEvent(student3, "Charlie", "charlie@test.com"), student3)
        };

        // Act
        var projections = events.BuildProjections<StudentProjection>(
            keySelector: e => e.Event.Tags.First(t => t.Key == "studentId").Value,
            applyEvent: StudentProjection.Apply
        ).ToList();

        // Assert
        Assert.Equal(3, projections.Count);

        var alice = projections.First(p => p.StudentId == student1);
        Assert.Equal("Alice", alice.Name);
        Assert.Equal("alice@test.com", alice.Email);
        Assert.Equal(2, alice.CourseCount);

        var bob = projections.First(p => p.StudentId == student2);
        Assert.Equal("Robert", bob.Name); // Name was changed
        Assert.Equal("bob@test.com", bob.Email);
        Assert.Equal(1, bob.CourseCount);

        var charlie = projections.First(p => p.StudentId == student3);
        Assert.Equal("Charlie", charlie.Name);
        Assert.Equal("charlie@test.com", charlie.Email);
        Assert.Equal(0, charlie.CourseCount);
    }

    [Fact]
    public void BuildProjections_WithPatternMatching_WorksCorrectly()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var events = new[]
        {
            CreateSequencedEvent(1, new StudentCreatedEvent(studentId, "Alice", "alice@test.com"), studentId),
            CreateSequencedEvent(2, new StudentEnrolledEvent(studentId, Guid.NewGuid()), studentId)
        };

        // Act - Using pattern matching like in the real usage
        var projections = events.BuildProjections<StudentProjection>(
            keySelector: e => e.Event.Tags.First(t => t.Key == "studentId").Value,
            applyEvent: (evt, current) => evt switch
            {
                StudentCreatedEvent created => new StudentProjection(
                    created.StudentId,
                    created.Name,
                    created.Email,
                    0),

                StudentEnrolledEvent _ when current != null =>
                    current with { CourseCount = current.CourseCount + 1 },

                _ => current!
            }
        ).ToList();

        // Assert
        Assert.Single(projections);
        Assert.Equal(1, projections[0].CourseCount);
    }

    #endregion

    #region Helper Methods

    private static SequencedEvent CreateSequencedEvent(long position, IEvent domainEvent, Guid studentId)
    {
        return new SequencedEvent
        {
            Position = position,
            Event = new DomainEvent
            {
                EventType = domainEvent.GetType().Name,
                Event = domainEvent,
                Tags = [new Tag { Key = "studentId", Value = studentId.ToString() }]
            },
            Metadata = new Metadata
            {
                Timestamp = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid()
            }
        };
    }

    #endregion
}
