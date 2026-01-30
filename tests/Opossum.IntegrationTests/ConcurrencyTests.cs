using Opossum.Core;
using Opossum.Exceptions;
using Opossum.Extensions;
using Opossum.Mediator;

namespace Opossum.IntegrationTests;

/// <summary>
/// Tests for concurrent operations and optimistic concurrency control using AppendCondition.
/// These tests validate the DCB specification's requirement that stale decision models must be rejected.
/// </summary>
public class ConcurrencyTests(OpossumFixture fixture) : IClassFixture<OpossumFixture>
{
    private readonly IMediator _mediator = fixture.Mediator;
    private readonly IEventStore _eventStore = fixture.EventStore;

    /// <summary>
    /// Scenario 1: Independent operations should execute successfully in parallel.
    /// RegisterStudentCommand and RenameCourseCommand have no overlapping decision models.
    /// </summary>
    [Fact]
    public async Task IndependentCommands_ShouldExecuteConcurrently_WithoutConflict()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var courseId = Guid.NewGuid();

        // Act - Execute commands in parallel (no overlapping decision models)
        var tasks = new[]
        {
            Task.Run(async () =>
            {
                var registerCommand = new RegisterStudentCommand(studentId, "John Doe");
                await _mediator.InvokeAsync<CommandResult>(registerCommand);
            }),
            Task.Run(async () =>
            {
                var createCourseCommand = new CreateCourseCommand(courseId, 30);
                await _mediator.InvokeAsync<CommandResult>(createCourseCommand);
            })
        };

        await Task.WhenAll(tasks);

        // Assert - Both events should exist
        var studentQuery = Query.FromEventTypes(nameof(StudentRegisteredEvent));
        var courseQuery = Query.FromEventTypes(nameof(CourseCreated));

        var studentEvents = await _eventStore.ReadAsync(studentQuery);
        var courseEvents = await _eventStore.ReadAsync(courseQuery);

        Assert.Contains(studentEvents, e => 
            ((StudentRegisteredEvent)e.Event.Event).StudentId == studentId);
        Assert.Contains(courseEvents, e => 
            ((CourseCreated)e.Event.Event).CourseId == courseId);
    }

    /// <summary>
    /// Scenario 2: CRITICAL TEST - Two concurrent enrollments when course has only 1 spot left.
    /// Only ONE should succeed, the other MUST fail with ConcurrencyException.
    /// This validates the DCB specification's optimistic concurrency control.
    /// </summary>
    [Fact]
    public async Task ConcurrentEnrollments_WhenCourseHasOneSpotLeft_ShouldAllowOnlyOne()
    {
        // Arrange - Create course with capacity of 10, enroll 9 students
        var courseId = Guid.NewGuid();
        await _mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(courseId, 10));

        // Enroll 9 students to fill 9 out of 10 spots
        for (int i = 0; i < 9; i++)
        {
            var studentId = Guid.NewGuid();
            await _mediator.InvokeAsync<CommandResult>(
                new EnrollStudentToCourseCommand(courseId, studentId));
        }

        // Verify we have 9 students enrolled
        var courseQuery = Query.FromTags(new Tag { Key = "courseId", Value = courseId.ToString() });
        var events = await _eventStore.ReadAsync(courseQuery);
        var enrolledCount = events.Count(e => e.Event.EventType == nameof(StudentEnrolledToCourseEvent));
        Assert.Equal(9, enrolledCount);

        // Act - Two students try to enroll simultaneously (both see 9 enrolled, both think they can enroll)
        var student10Id = Guid.NewGuid();
        var student11Id = Guid.NewGuid();

        var results = await Task.WhenAll(
            Task.Run(async () =>
            {
                try
                {
                    return await _mediator.InvokeAsync<CommandResult>(
                        new EnrollStudentToCourseCommand(courseId, student10Id));
                }
                catch (ConcurrencyException)
                {
                    return new CommandResult(false, "Concurrency conflict detected");
                }
            }),
            Task.Run(async () =>
            {
                try
                {
                    return await _mediator.InvokeAsync<CommandResult>(
                        new EnrollStudentToCourseCommand(courseId, student11Id));
                }
                catch (ConcurrencyException)
                {
                    return new CommandResult(false, "Concurrency conflict detected");
                }
            })
        );

        // Assert - Exactly ONE should succeed, ONE should fail
        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count(r => !r.Success);

        Assert.Equal(1, successCount);
        Assert.Equal(1, failureCount);

        // Verify final enrollment count is exactly 10
        events = await _eventStore.ReadAsync(courseQuery);
        enrolledCount = events.Count(e => e.Event.EventType == nameof(StudentEnrolledToCourseEvent));
        Assert.Equal(10, enrolledCount);
    }

    /// <summary>
    /// Scenario 3: Multiple concurrent enrollments to different courses should all succeed.
    /// This validates that locking is per-context and doesn't create false conflicts.
    /// </summary>
    [Fact]
    public async Task ConcurrentEnrollments_ToDifferentCourses_ShouldAllSucceed()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var course1Id = Guid.NewGuid();
        var course2Id = Guid.NewGuid();
        var course3Id = Guid.NewGuid();

        await _mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(course1Id, 30));
        await _mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(course2Id, 30));
        await _mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(course3Id, 30));

        // Act - Enroll same student to 3 different courses simultaneously
        var results = await Task.WhenAll(
            _mediator.InvokeAsync<CommandResult>(new EnrollStudentToCourseCommand(course1Id, studentId)),
            _mediator.InvokeAsync<CommandResult>(new EnrollStudentToCourseCommand(course2Id, studentId)),
            _mediator.InvokeAsync<CommandResult>(new EnrollStudentToCourseCommand(course3Id, studentId))
        );

        // Assert - All should succeed (student limit is 5, enrolling in 3 courses)
        Assert.All(results, result => Assert.True(result.Success));

        // Verify student is in all 3 courses
        var studentQuery = Query.FromItems(
            new QueryItem
            {
                Tags = [new Tag { Key = "studentId", Value = studentId.ToString() }],
                EventTypes = [nameof(StudentEnrolledToCourseEvent)]
            }
        );

        var studentEvents = await _eventStore.ReadAsync(studentQuery);
        Assert.Equal(3, studentEvents.Length);
    }

    /// <summary>
    /// Scenario 4: Same student trying to enroll in same course twice should fail.
    /// This tests idempotency and duplicate detection.
    /// </summary>
    [Fact]
    public async Task ConcurrentEnrollments_SameStudentSameCourse_ShouldOnlyAllowOnce()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var courseId = Guid.NewGuid();

        await _mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(courseId, 30));

        // Act - Try to enroll same student twice simultaneously
        var results = await Task.WhenAll(
            Task.Run(() => _mediator.InvokeAsync<CommandResult>(
                new EnrollStudentToCourseCommand(courseId, studentId))),
            Task.Run(() => _mediator.InvokeAsync<CommandResult>(
                new EnrollStudentToCourseCommand(courseId, studentId)))
        );

        // Assert - At least one should succeed
        Assert.Contains(results, r => r.Success);

        // Verify only one enrollment event exists for this student-course combination
        var query = Query.FromItems(
            new QueryItem
            {
                Tags = [
                    new Tag { Key = "courseId", Value = courseId.ToString() },
                    new Tag { Key = "studentId", Value = studentId.ToString() }
                ],
                EventTypes = [nameof(StudentEnrolledToCourseEvent)]
            }
        );

        var events = await _eventStore.ReadAsync(query);
        
        // Should only have 1 enrollment event (not 2)
        // The second attempt should either:
        // - Fail due to concurrency check
        // - Succeed but be recognized as already enrolled (business logic)
        Assert.True(events.Length <= 1, 
            $"Expected at most 1 enrollment event, but found {events.Length}");
    }

    /// <summary>
    /// Scenario 5: Stress test - Many concurrent enrollments to same course.
    /// Only the first N (up to capacity) should succeed.
    /// </summary>
    [Fact]
    public async Task ConcurrentEnrollments_ManyStudentsOneCourse_ShouldRespectCapacity()
    {
        // Arrange
        var courseId = Guid.NewGuid();
        var capacity = 10;
        var attemptCount = 20; // 20 students try to enroll in course with capacity of 10

        await _mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(courseId, capacity));

        // Act - 20 students try to enroll simultaneously
        var tasks = Enumerable.Range(0, attemptCount)
            .Select(_ => Guid.NewGuid()) // Generate 20 different student IDs
            .Select(studentId => Task.Run(async () =>
            {
                try
                {
                    return await _mediator.InvokeAsync<CommandResult>(
                        new EnrollStudentToCourseCommand(courseId, studentId));
                }
                catch (ConcurrencyException)
                {
                    return new CommandResult(false, "Concurrency conflict");
                }
            }))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert - Exactly 10 should succeed, 10 should fail
        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count(r => !r.Success);

        Assert.Equal(capacity, successCount);
        Assert.Equal(attemptCount - capacity, failureCount);

        // Verify exactly 10 enrollment events exist
        var courseQuery = Query.FromTags(new Tag { Key = "courseId", Value = courseId.ToString() });
        var events = await _eventStore.ReadAsync(courseQuery);
        var enrolledCount = events.Count(e => e.Event.EventType == nameof(StudentEnrolledToCourseEvent));

        Assert.Equal(capacity, enrolledCount);
    }

    /// <summary>
    /// Scenario 6: Test that failed operations release the lock properly.
    /// A failed append should not block subsequent operations.
    /// </summary>
    [Fact]
    public async Task FailedAppend_ShouldReleaseLock_AllowingSubsequentOperations()
    {
        // Arrange
        var courseId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

        await _mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(courseId, 1));

        // Act - First enrollment succeeds
        var result1 = await _mediator.InvokeAsync<CommandResult>(
            new EnrollStudentToCourseCommand(courseId, studentId));

        Assert.True(result1.Success);

        // Second enrollment fails (course full)
        var student2Id = Guid.NewGuid();
        var result2 = await _mediator.InvokeAsync<CommandResult>(
            new EnrollStudentToCourseCommand(courseId, student2Id));

        Assert.False(result2.Success);

        // Third operation should work (proves lock was released after failure)
        var student3Id = Guid.NewGuid();
        var course2Id = Guid.NewGuid();
        await _mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(course2Id, 1));

        var result3 = await _mediator.InvokeAsync<CommandResult>(
            new EnrollStudentToCourseCommand(course2Id, student3Id));

        Assert.True(result3.Success); // Should succeed - proves lock works correctly
    }

    /// <summary>
    /// Scenario 7: Test AppendCondition with AfterSequencePosition.
    /// Direct EventStore test without mediator.
    /// </summary>
    [Fact]
    public async Task AppendAsync_WithAfterSequencePosition_ShouldDetectStaleReads()
    {
        // Arrange
        var courseId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

        // Create initial event
        var createEvent = new SequencedEvent
        {
            Event = new DomainEvent
            {
                EventType = nameof(CourseCreated),
                Event = new CourseCreated(courseId, 10),
                Tags = [new Tag { Key = "courseId", Value = courseId.ToString() }]
            },
            Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
        };

        await _eventStore.AppendAsync([createEvent], null);

        // Read events and note the position
        var query = Query.FromTags(new Tag { Key = "courseId", Value = courseId.ToString() });
        var events = await _eventStore.ReadAsync(query);
        var lastPosition = events[^1].Position;

        // Another operation appends an event (simulating concurrent modification)
        var concurrentEvent = new SequencedEvent
        {
            Event = new DomainEvent
            {
                EventType = nameof(StudentEnrolledToCourseEvent),
                Event = new StudentEnrolledToCourseEvent(courseId, Guid.NewGuid()),
                Tags = [new Tag { Key = "courseId", Value = courseId.ToString() }]
            },
            Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
        };

        await _eventStore.AppendAsync([concurrentEvent], null);

        // Act - Try to append with stale AfterSequencePosition
        var appendCondition = new AppendCondition
        {
            AfterSequencePosition = lastPosition, // This is now stale!
            FailIfEventsMatch = Query.FromItems() // Empty query - we're only testing AfterSequencePosition
        };

        var newEvent = new SequencedEvent
        {
            Event = new DomainEvent
            {
                EventType = nameof(StudentEnrolledToCourseEvent),
                Event = new StudentEnrolledToCourseEvent(courseId, studentId),
                Tags = [new Tag { Key = "courseId", Value = courseId.ToString() }]
            },
            Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
        };

        // Assert - Should throw ConcurrencyException
        await Assert.ThrowsAsync<ConcurrencyException>(async () =>
            await _eventStore.AppendAsync([newEvent], appendCondition));
    }

    /// <summary>
    /// Scenario 8: Test AppendCondition with FailIfEventsMatch.
    /// Direct EventStore test without mediator.
    /// </summary>
    [Fact]
    public async Task AppendAsync_WithFailIfEventsMatch_ShouldDetectConflictingEvents()
    {
        // Arrange
        var courseId = Guid.NewGuid();

        // Create initial event
        var createEvent = new SequencedEvent
        {
            Event = new DomainEvent
            {
                EventType = nameof(CourseCreated),
                Event = new CourseCreated(courseId, 10),
                Tags = [new Tag { Key = "courseId", Value = courseId.ToString() }]
            },
            Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
        };

        await _eventStore.AppendAsync([createEvent], null);

        // Append a conflicting event
        var enrollEvent = new SequencedEvent
        {
            Event = new DomainEvent
            {
                EventType = nameof(StudentEnrolledToCourseEvent),
                Event = new StudentEnrolledToCourseEvent(courseId, Guid.NewGuid()),
                Tags = [new Tag { Key = "courseId", Value = courseId.ToString() }]
            },
            Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
        };

        await _eventStore.AppendAsync([enrollEvent], null);

        // Act - Try to append with condition that should fail
        var conflictQuery = Query.FromItems(
            new QueryItem
            {
                Tags = [new Tag { Key = "courseId", Value = courseId.ToString() }],
                EventTypes = [nameof(StudentEnrolledToCourseEvent)]
            }
        );

        var appendCondition = new AppendCondition
        {
            FailIfEventsMatch = conflictQuery // This will match the existing enrollment event
        };

        var newEvent = new SequencedEvent
        {
            Event = new DomainEvent
            {
                EventType = nameof(StudentEnrolledToCourseEvent),
                Event = new StudentEnrolledToCourseEvent(courseId, Guid.NewGuid()),
                Tags = [new Tag { Key = "courseId", Value = courseId.ToString() }]
            },
            Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
        };

        // Assert - Should throw ConcurrencyException
        await Assert.ThrowsAsync<ConcurrencyException>(async () =>
            await _eventStore.AppendAsync([newEvent], appendCondition));
    }
}

// ============================================================================
// ADDITIONAL COMMAND DEFINITIONS FOR CONCURRENCY TESTS
// ============================================================================

public record RegisterStudentCommand(Guid StudentId, string Name);
public record StudentRegisteredEvent(Guid StudentId, string Name) : IEvent;
