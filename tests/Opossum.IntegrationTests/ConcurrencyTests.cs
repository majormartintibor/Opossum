using Opossum.Core;
using Opossum.Exceptions;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.IntegrationTests.Fixtures;

namespace Opossum.IntegrationTests;

/// <summary>
/// Tests for concurrent operations and optimistic concurrency control using AppendCondition.
/// These tests validate the DCB specification's requirement that stale decision models must be rejected.
/// 
/// Uses IntegrationTestCollection to prevent parallel test execution and ensure proper file system isolation.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class ConcurrencyTests(OpossumFixture fixture) : IClassFixture<OpossumFixture>
{
    private readonly OpossumFixture _fixture = fixture;

    /// <summary>
    /// Scenario 1: Independent operations should execute successfully in parallel.
    /// RegisterStudentCommand and RenameCourseCommand have no overlapping decision models.
    /// Uses isolated scope for proper file system isolation.
    /// </summary>
    [Fact]
    public async Task IndependentCommands_ShouldExecuteConcurrently_WithoutConflict()
    {
        // Arrange - Use isolated scope
        using var scope = _fixture.GetIsolatedServiceScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();

        var studentId = Guid.NewGuid();
        var courseId = Guid.NewGuid();

        // Act - Execute commands in parallel (no overlapping decision models)
        var tasks = new[]
        {
            Task.Run(async () =>
            {
                var registerCommand = new RegisterStudentCommand(studentId, "John Doe");
                await mediator.InvokeAsync<CommandResult>(registerCommand);
            }),
            Task.Run(async () =>
            {
                var createCourseCommand = new CreateCourseCommand(courseId, 30);
                await mediator.InvokeAsync<CommandResult>(createCourseCommand);
            })
        };

        await Task.WhenAll(tasks);

        // Assert - Both events should exist
        var studentQuery = Query.FromEventTypes(nameof(StudentRegisteredEvent));
        var courseQuery = Query.FromEventTypes(nameof(CourseCreated));

        var studentEvents = await eventStore.ReadAsync(studentQuery);
        var courseEvents = await eventStore.ReadAsync(courseQuery);

        Assert.Contains(studentEvents, e =>
            ((StudentRegisteredEvent)e.Event.Event).StudentId == studentId);
        Assert.Contains(courseEvents, e =>
            ((CourseCreated)e.Event.Event).CourseId == courseId);
    }

    /// <summary>
    /// Scenario 2: CRITICAL TEST - Two concurrent enrollments when course has only 1 spot left.
    /// Only ONE should succeed, the other MUST fail with ConcurrencyException.
    /// This validates the DCB specification's optimistic concurrency control.
    /// Uses isolated scope for proper file system isolation during concurrent operations.
    /// </summary>
    [Fact]
    public async Task ConcurrentEnrollments_WhenCourseHasOneSpotLeft_ShouldAllowOnlyOne()
    {
        // Arrange - Use isolated scope to avoid file locking conflicts
        using var scope = _fixture.GetIsolatedServiceScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();

        // Create course with capacity of 10, enroll 9 students
        var courseId = Guid.NewGuid();
        await mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(courseId, 10));

        // Enroll 9 students to fill 9 out of 10 spots
        for (int i = 0; i < 9; i++)
        {
            var studentId = Guid.NewGuid();
            await mediator.InvokeAsync<CommandResult>(
                new EnrollStudentToCourseCommand(courseId, studentId));
        }

        // Verify we have 9 students enrolled
        var courseQuery = Query.FromTags(new Tag("courseId", courseId.ToString()));
        var events = await eventStore.ReadAsync(courseQuery);
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
                    return await mediator.InvokeAsync<CommandResult>(
                        new EnrollStudentToCourseCommand(courseId, student10Id));
                }
                catch (AppendConditionFailedException)
                {
                    return new CommandResult(false, "Concurrency conflict detected");
                }
            }),
            Task.Run(async () =>
            {
                try
                {
                    return await mediator.InvokeAsync<CommandResult>(
                        new EnrollStudentToCourseCommand(courseId, student11Id));
                }
                catch (AppendConditionFailedException)
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
        events = await eventStore.ReadAsync(courseQuery);
        enrolledCount = events.Count(e => e.Event.EventType == nameof(StudentEnrolledToCourseEvent));
        Assert.Equal(10, enrolledCount);
    }

    /// <summary>
    /// Scenario 3: Multiple concurrent enrollments to different courses should all succeed.
    /// This validates that locking is per-context and doesn't create false conflicts.
    /// Uses isolated scope for proper file system isolation.
    /// </summary>
    [Fact]
    public async Task ConcurrentEnrollments_ToDifferentCourses_ShouldAllSucceed()
    {
        // Arrange - Use isolated scope
        using var scope = _fixture.GetIsolatedServiceScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();

        var studentId = Guid.NewGuid();
        var course1Id = Guid.NewGuid();
        var course2Id = Guid.NewGuid();
        var course3Id = Guid.NewGuid();

        await mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(course1Id, 30));
        await mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(course2Id, 30));
        await mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(course3Id, 30));

        // Act - Enroll same student to 3 different courses simultaneously
        var results = await Task.WhenAll(
            mediator.InvokeAsync<CommandResult>(new EnrollStudentToCourseCommand(course1Id, studentId)),
            mediator.InvokeAsync<CommandResult>(new EnrollStudentToCourseCommand(course2Id, studentId)),
            mediator.InvokeAsync<CommandResult>(new EnrollStudentToCourseCommand(course3Id, studentId))
        );

        // Assert - All should succeed (student limit is 5, enrolling in 3 courses)
        Assert.All(results, result => Assert.True(result.Success));

        // Verify student is in all 3 courses
        var studentQuery = Query.FromItems(
            new QueryItem
            {
                Tags = [new Tag("studentId", studentId.ToString())],
                EventTypes = [nameof(StudentEnrolledToCourseEvent)]
            }
        );

        var studentEvents = await eventStore.ReadAsync(studentQuery);
        Assert.Equal(3, studentEvents.Length);
    }

    /// <summary>
    /// Scenario 4: Same student trying to enroll in same course twice should fail.
    /// This tests idempotency and duplicate detection.
    /// Uses isolated scope for proper file system isolation.
    /// </summary>
    [Fact]
    public async Task ConcurrentEnrollments_SameStudentSameCourse_ShouldOnlyAllowOnce()
    {
        // Arrange - Use isolated scope
        using var scope = _fixture.GetIsolatedServiceScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();

        var studentId = Guid.NewGuid();
        var courseId = Guid.NewGuid();

        await mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(courseId, 30));

        // Act - Try to enroll same student twice simultaneously
        var results = await Task.WhenAll(
            Task.Run(() => mediator.InvokeAsync<CommandResult>(
                new EnrollStudentToCourseCommand(courseId, studentId))),
            Task.Run(() => mediator.InvokeAsync<CommandResult>(
                new EnrollStudentToCourseCommand(courseId, studentId)))
        );

        // Assert - At least one should succeed
        Assert.Contains(results, r => r.Success);

        // Verify only one enrollment event exists for this student-course combination
        var query = Query.FromItems(
            new QueryItem
            {
                Tags = [
                    new Tag("courseId", courseId.ToString()),
                    new Tag("studentId", studentId.ToString())
                ],
                EventTypes = [nameof(StudentEnrolledToCourseEvent)]
            }
        );

        var events = await eventStore.ReadAsync(query);

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
    /// Uses isolated service scope to prevent file locking issues during concurrent operations.
    /// </summary>
    [Fact]
    public async Task ConcurrentEnrollments_ManyStudentsOneCourse_ShouldRespectCapacity()
    {
        // Arrange - Use isolated scope to avoid file locking conflicts
        using var scope = _fixture.GetIsolatedServiceScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();

        var courseId = Guid.NewGuid();
        var capacity = 10;
        var attemptCount = 20; // 20 students try to enroll in course with capacity of 10

        await mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(courseId, capacity));

        // Act - 20 students try to enroll simultaneously
        var tasks = Enumerable.Range(0, attemptCount)
            .Select(_ => Guid.NewGuid()) // Generate 20 different student IDs
            .Select(studentId => Task.Run(async () =>
            {
                try
                {
                    return await mediator.InvokeAsync<CommandResult>(
                        new EnrollStudentToCourseCommand(courseId, studentId));
                }
                catch (AppendConditionFailedException)
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
        var courseQuery = Query.FromTags(new Tag("courseId", courseId.ToString()));
        var events = await eventStore.ReadAsync(courseQuery);
        var enrolledCount = events.Count(e => e.Event.EventType == nameof(StudentEnrolledToCourseEvent));

        Assert.Equal(capacity, enrolledCount);
    }

    /// <summary>
    /// Scenario 6: Test that failed operations release the lock properly.
    /// A failed append should not block subsequent operations.
    /// Uses isolated scope for proper file system isolation.
    /// </summary>
    [Fact]
    public async Task FailedAppend_ShouldReleaseLock_AllowingSubsequentOperations()
    {
        // Arrange - Use isolated scope
        using var scope = _fixture.GetIsolatedServiceScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var courseId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

        await mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(courseId, 1));

        // Act - First enrollment succeeds
        var result1 = await mediator.InvokeAsync<CommandResult>(
            new EnrollStudentToCourseCommand(courseId, studentId));

        Assert.True(result1.Success);

        // Second enrollment fails (course full)
        var student2Id = Guid.NewGuid();
        var result2 = await mediator.InvokeAsync<CommandResult>(
            new EnrollStudentToCourseCommand(courseId, student2Id));

        Assert.False(result2.Success);

        // Third operation should work (proves lock was released after failure)
        var student3Id = Guid.NewGuid();
        var course2Id = Guid.NewGuid();
        await mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(course2Id, 1));

        var result3 = await mediator.InvokeAsync<CommandResult>(
            new EnrollStudentToCourseCommand(course2Id, student3Id));

        Assert.True(result3.Success); // Should succeed - proves lock works correctly
    }

    /// <summary>
    /// Scenario 7: Test AppendCondition with AfterSequencePosition.
    /// Direct EventStore test without mediator.
    /// Uses isolated scope for proper file system isolation.
    /// </summary>
    [Fact]
    public async Task AppendAsync_WithAfterSequencePosition_ShouldDetectStaleReads()
    {
        // Arrange - Use isolated scope
        using var scope = _fixture.GetIsolatedServiceScope();
        var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();

        var courseId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

        // Create initial event
        var createEvent = new NewEvent
        {
            Event = new DomainEvent
            {
                EventType = nameof(CourseCreated),
                Event = new CourseCreated(courseId, 10),
                Tags = [new Tag("courseId", courseId.ToString())]
            },
            Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
        };

        await eventStore.AppendAsync([createEvent], null);

        // Read events and note the position
        var query = Query.FromTags(new Tag("courseId", courseId.ToString()));
        var events = await eventStore.ReadAsync(query);
        var lastPosition = events[^1].Position;

        // Another operation appends an event (simulating concurrent modification)
        var concurrentEvent = new NewEvent
        {
            Event = new DomainEvent
            {
                EventType = nameof(StudentEnrolledToCourseEvent),
                Event = new StudentEnrolledToCourseEvent(courseId, Guid.NewGuid()),
                Tags = [new Tag("courseId", courseId.ToString())]
            },
            Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
        };

        await eventStore.AppendAsync([concurrentEvent], null);

        // Act - Try to append with stale AfterSequencePosition
        var appendCondition = new AppendCondition
        {
            AfterSequencePosition = lastPosition, // This is now stale!
            FailIfEventsMatch = Query.FromItems() // Empty query - we're only testing AfterSequencePosition
        };

        var newEvent = new NewEvent
        {
            Event = new DomainEvent
            {
                EventType = nameof(StudentEnrolledToCourseEvent),
                Event = new StudentEnrolledToCourseEvent(courseId, studentId),
                Tags = [new Tag("courseId", courseId.ToString())]
            },
            Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
        };

        // Assert - Should throw ConcurrencyException (subclass of AppendConditionFailedException)
        await Assert.ThrowsAsync<ConcurrencyException>(async () =>
            await eventStore.AppendAsync([newEvent], appendCondition));
    }

    /// <summary>
    /// Scenario 8: Test AppendCondition with FailIfEventsMatch.
    /// Direct EventStore test without mediator.
    /// Uses isolated scope for proper file system isolation.
    /// </summary>
    [Fact]
    public async Task AppendAsync_WithFailIfEventsMatch_ShouldDetectConflictingEvents()
    {
        // Arrange - Use isolated scope
        using var scope = _fixture.GetIsolatedServiceScope();
        var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();

        var courseId = Guid.NewGuid();

        // Create initial event
        var createEvent = new NewEvent
        {
            Event = new DomainEvent
            {
                EventType = nameof(CourseCreated),
                Event = new CourseCreated(courseId, 10),
                Tags = [new Tag("courseId", courseId.ToString())]
            },
            Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
        };

        await eventStore.AppendAsync([createEvent], null);

        // Append a conflicting event
        var enrollEvent = new NewEvent
        {
            Event = new DomainEvent
            {
                EventType = nameof(StudentEnrolledToCourseEvent),
                Event = new StudentEnrolledToCourseEvent(courseId, Guid.NewGuid()),
                Tags = [new Tag("courseId", courseId.ToString())]
            },
            Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
        };

        await eventStore.AppendAsync([enrollEvent], null);

        // Act - Try to append with condition that should fail
        var conflictQuery = Query.FromItems(
            new QueryItem
            {
                Tags = [new Tag("courseId", courseId.ToString())],
                EventTypes = [nameof(StudentEnrolledToCourseEvent)]
            }
        );

        var appendCondition = new AppendCondition
        {
            FailIfEventsMatch = conflictQuery // This will match the existing enrollment event
        };

        var newEvent = new NewEvent
        {
            Event = new DomainEvent
            {
                EventType = nameof(StudentEnrolledToCourseEvent),
                Event = new StudentEnrolledToCourseEvent(courseId, Guid.NewGuid()),
                Tags = [new Tag("courseId", courseId.ToString())]
            },
            Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
        };

        // Assert - Should throw ConcurrencyException (subclass of AppendConditionFailedException)
        await Assert.ThrowsAsync<ConcurrencyException>(async () =>
            await eventStore.AppendAsync([newEvent], appendCondition));
    }
}

// ============================================================================
// ADDITIONAL COMMAND DEFINITIONS FOR CONCURRENCY TESTS
// ============================================================================

public record RegisterStudentCommand(Guid StudentId, string Name);
public record StudentRegisteredEvent(Guid StudentId, string Name) : IEvent;
