using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.DependencyInjection;
using Opossum.Exceptions;
using Opossum.Extensions;

namespace Opossum.IntegrationTests.DecisionModel;

/// <summary>
/// Integration tests for <see cref="DecisionModelExtensions.BuildDecisionModelAsync{TState}"/>.
/// Each test uses a completely isolated temp directory — no shared state, no collection needed.
/// </summary>
public sealed class BuildDecisionModelIntegrationTests : IDisposable
{
    private readonly string _baseTempPath;
    private readonly List<ServiceProvider> _serviceProviders = [];

    public BuildDecisionModelIntegrationTests()
    {
        _baseTempPath = Path.Combine(
            Path.GetTempPath(),
            "OpossumDecisionModelIntegrationTests",
            Guid.NewGuid().ToString());
    }

    public void Dispose()
    {
        foreach (var sp in _serviceProviders)
            sp.Dispose();

        if (Directory.Exists(_baseTempPath))
        {
            try { Directory.Delete(_baseTempPath, recursive: true); }
            catch { /* ignore cleanup errors */ }
        }
    }

    private IEventStore CreateEventStore()
    {
        var path = Path.Combine(_baseTempPath, Guid.NewGuid().ToString());
        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = path;
            opt.FlushEventsImmediately = false;
            opt.AddContext("TestContext");
        });
        var sp = services.BuildServiceProvider();
        _serviceProviders.Add(sp);
        return sp.GetRequiredService<IEventStore>();
    }

    #region Test domain events

    private record CourseCreatedEvent(Guid CourseId, int MaxStudents) : IEvent;
    private record StudentEnrolledEvent(Guid CourseId, Guid StudentId) : IEvent;
    private record UnrelatedEvent(string Data) : IEvent;

    #endregion

    #region Helper projections

    private static IDecisionProjection<bool> CourseExistsProjection(Guid courseId) =>
        new DecisionProjection<bool>(
            initialState: false,
            query: Query.FromItems(new QueryItem
            {
                EventTypes = [nameof(CourseCreatedEvent)],
                Tags = [new Tag { Key = "courseId", Value = courseId.ToString() }]
            }),
            apply: (_, evt) => evt.Event.Event is CourseCreatedEvent ? true : false);

    private static IDecisionProjection<int> EnrollmentCountProjection(Guid courseId) =>
        new DecisionProjection<int>(
            initialState: 0,
            query: Query.FromItems(new QueryItem
            {
                EventTypes = [nameof(StudentEnrolledEvent)],
                Tags = [new Tag { Key = "courseId", Value = courseId.ToString() }]
            }),
            apply: (count, evt) => evt.Event.Event is StudentEnrolledEvent ? count + 1 : count);

    #endregion

    #region Empty store

    [Fact]
    public async Task BuildDecisionModelAsync_EmptyStore_StateIsInitialState()
    {
        var store = CreateEventStore();
        var courseId = Guid.NewGuid();

        var model = await store.BuildDecisionModelAsync(CourseExistsProjection(courseId));

        Assert.False(model.State);
    }

    [Fact]
    public async Task BuildDecisionModelAsync_EmptyStore_AfterSequencePositionIsNull()
    {
        var store = CreateEventStore();
        var courseId = Guid.NewGuid();

        var model = await store.BuildDecisionModelAsync(CourseExistsProjection(courseId));

        Assert.Null(model.AppendCondition.AfterSequencePosition);
    }

    [Fact]
    public async Task BuildDecisionModelAsync_EmptyStore_FailIfEventsMatchIsProjectionQuery()
    {
        var store = CreateEventStore();
        var courseId = Guid.NewGuid();
        var projection = CourseExistsProjection(courseId);

        var model = await store.BuildDecisionModelAsync(projection);

        Assert.Same(projection.Query, model.AppendCondition.FailIfEventsMatch);
    }

    #endregion

    #region State reflects appended events

    [Fact]
    public async Task BuildDecisionModelAsync_AfterAppend_StateReflectsEvents()
    {
        var store = CreateEventStore();
        var courseId = Guid.NewGuid();

        await store.AppendAsync(
            new CourseCreatedEvent(courseId, 30)
                .ToDomainEvent()
                .WithTag("courseId", courseId.ToString()));

        var model = await store.BuildDecisionModelAsync(CourseExistsProjection(courseId));

        Assert.True(model.State);
    }

    [Fact]
    public async Task BuildDecisionModelAsync_AfterMultipleEnrollments_CountIsCorrect()
    {
        var store = CreateEventStore();
        var courseId = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
        {
            await store.AppendAsync(
                new StudentEnrolledEvent(courseId, Guid.NewGuid())
                    .ToDomainEvent()
                    .WithTag("courseId", courseId.ToString()));
        }

        var model = await store.BuildDecisionModelAsync(EnrollmentCountProjection(courseId));

        Assert.Equal(3, model.State);
    }

    [Fact]
    public async Task BuildDecisionModelAsync_AfterSequencePosition_IsMaxEventPosition()
    {
        var store = CreateEventStore();
        var courseId = Guid.NewGuid();

        await store.AppendAsync(
            new CourseCreatedEvent(courseId, 30)
                .ToDomainEvent()
                .WithTag("courseId", courseId.ToString()));

        await store.AppendAsync(
            new StudentEnrolledEvent(courseId, Guid.NewGuid())
                .ToDomainEvent()
                .WithTag("courseId", courseId.ToString()));

        var model = await store.BuildDecisionModelAsync(EnrollmentCountProjection(courseId));

        Assert.Equal(2, model.AppendCondition.AfterSequencePosition);
    }

    [Fact]
    public async Task BuildDecisionModelAsync_UnrelatedEventsDoNotAffectState()
    {
        var store = CreateEventStore();
        var courseId = Guid.NewGuid();
        var otherCourseId = Guid.NewGuid();

        // Append events for a different course — should be invisible to our projection
        await store.AppendAsync(
            new CourseCreatedEvent(otherCourseId, 10)
                .ToDomainEvent()
                .WithTag("courseId", otherCourseId.ToString()));

        var model = await store.BuildDecisionModelAsync(CourseExistsProjection(courseId));

        Assert.False(model.State);
        Assert.Null(model.AppendCondition.AfterSequencePosition);
    }

    #endregion

    #region DCB round-trip: AppendCondition enforces consistency

    [Fact]
    public async Task BuildDecisionModelAsync_StaleCondition_AppendThrows()
    {
        // Arrange: append initial event, build decision model
        var store = CreateEventStore();
        var courseId = Guid.NewGuid();

        await store.AppendAsync(
            new CourseCreatedEvent(courseId, 1)
                .ToDomainEvent()
                .WithTag("courseId", courseId.ToString()));

        var staleModel = await store.BuildDecisionModelAsync(EnrollmentCountProjection(courseId));

        // Simulate concurrent write: another client enrols a student
        await store.AppendAsync(
            new StudentEnrolledEvent(courseId, Guid.NewGuid())
                .ToDomainEvent()
                .WithTag("courseId", courseId.ToString()));

        // Act & Assert: our stale AppendCondition must be rejected
        var newEvent = new StudentEnrolledEvent(courseId, Guid.NewGuid())
            .ToDomainEvent()
            .WithTag("courseId", courseId.ToString());

        await Assert.ThrowsAnyAsync<EventStoreException>(async () =>
            await store.AppendAsync(newEvent, staleModel.AppendCondition));
    }

    [Fact]
    public async Task BuildDecisionModelAsync_FreshCondition_AppendSucceeds()
    {
        // Arrange: append initial event, build a FRESH decision model
        var store = CreateEventStore();
        var courseId = Guid.NewGuid();

        await store.AppendAsync(
            new CourseCreatedEvent(courseId, 30)
                .ToDomainEvent()
                .WithTag("courseId", courseId.ToString()));

        var freshModel = await store.BuildDecisionModelAsync(EnrollmentCountProjection(courseId));

        // Act: no concurrent write — condition should pass
        var newEvent = new StudentEnrolledEvent(courseId, Guid.NewGuid())
            .ToDomainEvent()
            .WithTag("courseId", courseId.ToString());

        var exception = await Record.ExceptionAsync(async () =>
            await store.AppendAsync(newEvent, freshModel.AppendCondition));

        Assert.Null(exception);
    }

    [Fact]
    public async Task BuildDecisionModelAsync_UnrelatedConcurrentWrite_DoesNotInvalidateCondition()
    {
        // The DCB boundary is scoped — unrelated events must not cause conflicts
        var store = CreateEventStore();
        var courseId = Guid.NewGuid();
        var otherCourseId = Guid.NewGuid();

        await store.AppendAsync(
            new CourseCreatedEvent(courseId, 30)
                .ToDomainEvent()
                .WithTag("courseId", courseId.ToString()));

        var model = await store.BuildDecisionModelAsync(EnrollmentCountProjection(courseId));

        // Concurrent write to a completely different course
        await store.AppendAsync(
            new StudentEnrolledEvent(otherCourseId, Guid.NewGuid())
                .ToDomainEvent()
                .WithTag("courseId", otherCourseId.ToString()));

        // Our condition covers courseId only — the write above must not invalidate it
        var newEvent = new StudentEnrolledEvent(courseId, Guid.NewGuid())
            .ToDomainEvent()
            .WithTag("courseId", courseId.ToString());

        var exception = await Record.ExceptionAsync(async () =>
            await store.AppendAsync(newEvent, model.AppendCondition));

        Assert.Null(exception);
    }

    #endregion

    #region Cancellation

    [Fact]
    public async Task BuildDecisionModelAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var store = CreateEventStore();
        var projection = CourseExistsProjection(Guid.NewGuid());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.BuildDecisionModelAsync(projection, cts.Token));
    }

    #endregion

    #region Guard clauses

    [Fact]
    public async Task BuildDecisionModelAsync_NullEventStore_ThrowsArgumentNullException()
    {
        var projection = CourseExistsProjection(Guid.NewGuid());

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await ((IEventStore)null!).BuildDecisionModelAsync(projection));
    }

    [Fact]
    public async Task BuildDecisionModelAsync_NullProjection_ThrowsArgumentNullException()
    {
        var store = CreateEventStore();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await store.BuildDecisionModelAsync((IDecisionProjection<bool>)null!));
    }

    #endregion

    #region Composed 2-projection overload

    [Fact]
    public async Task Compose2_BothStatesReflectAppendedEvents()
    {
        var store = CreateEventStore();
        var courseId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

        await store.AppendAsync(
            new CourseCreatedEvent(courseId, 30)
                .ToDomainEvent()
                .WithTag("courseId", courseId.ToString()));

        await store.AppendAsync(
            new StudentEnrolledEvent(courseId, studentId)
                .ToDomainEvent()
                .WithTag("courseId", courseId.ToString()));

        var (courseExists, enrollmentCount, _) = await store.BuildDecisionModelAsync(
            CourseExistsProjection(courseId),
            EnrollmentCountProjection(courseId));

        Assert.True(courseExists);
        Assert.Equal(1, enrollmentCount);
    }

    [Fact]
    public async Task Compose2_SingleReadForBothProjections_ConditionSpansBothQueries()
    {
        var store = CreateEventStore();
        var courseId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

        await store.AppendAsync(
            new CourseCreatedEvent(courseId, 30)
                .ToDomainEvent()
                .WithTag("courseId", courseId.ToString()));

        var (_, _, condition) = await store.BuildDecisionModelAsync(
            CourseExistsProjection(courseId),
            EnrollmentCountProjection(courseId));

        Assert.NotNull(condition.AfterSequencePosition);
        Assert.NotNull(condition.FailIfEventsMatch);
        // Union query must have items from both sub-queries
        Assert.True(condition.FailIfEventsMatch.QueryItems.Count >= 2);
    }

    [Fact]
    public async Task Compose2_StaleCondition_AppendThrows()
    {
        var store = CreateEventStore();
        var courseId = Guid.NewGuid();

        await store.AppendAsync(
            new CourseCreatedEvent(courseId, 1)
                .ToDomainEvent()
                .WithTag("courseId", courseId.ToString()));

        var (_, _, staleCondition) = await store.BuildDecisionModelAsync(
            CourseExistsProjection(courseId),
            EnrollmentCountProjection(courseId));

        // Concurrent write against one of the queries
        await store.AppendAsync(
            new StudentEnrolledEvent(courseId, Guid.NewGuid())
                .ToDomainEvent()
                .WithTag("courseId", courseId.ToString()));

        var newEvent = new StudentEnrolledEvent(courseId, Guid.NewGuid())
            .ToDomainEvent()
            .WithTag("courseId", courseId.ToString());

        await Assert.ThrowsAnyAsync<EventStoreException>(async () =>
            await store.AppendAsync(newEvent, staleCondition));
    }

    [Fact]
    public async Task Compose2_UnrelatedConcurrentWrite_DoesNotInvalidateCondition()
    {
        var store = CreateEventStore();
        var courseId = Guid.NewGuid();
        var otherCourseId = Guid.NewGuid();

        await store.AppendAsync(
            new CourseCreatedEvent(courseId, 30)
                .ToDomainEvent()
                .WithTag("courseId", courseId.ToString()));

        var (_, _, condition) = await store.BuildDecisionModelAsync(
            CourseExistsProjection(courseId),
            EnrollmentCountProjection(courseId));

        // Unrelated write — different courseId, outside our composed query scope
        await store.AppendAsync(
            new StudentEnrolledEvent(otherCourseId, Guid.NewGuid())
                .ToDomainEvent()
                .WithTag("courseId", otherCourseId.ToString()));

        var newEvent = new StudentEnrolledEvent(courseId, Guid.NewGuid())
            .ToDomainEvent()
            .WithTag("courseId", courseId.ToString());

        var exception = await Record.ExceptionAsync(async () =>
            await store.AppendAsync(newEvent, condition));

        Assert.Null(exception);
    }

    #endregion

    #region Composed 3-projection overload

    [Fact]
    public async Task Compose3_AllThreeStatesReflectAppendedEvents()
    {
        var store = CreateEventStore();
        var courseId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

        var studentExistsProjection = new DecisionProjection<bool>(
            initialState: false,
            query: Query.FromItems(new QueryItem
            {
                EventTypes = [nameof(StudentEnrolledEvent)],
                Tags = [new Tag { Key = "studentId", Value = studentId.ToString() }]
            }),
            apply: (_, evt) => evt.Event.Event is StudentEnrolledEvent ? true : false);

        await store.AppendAsync(
            new CourseCreatedEvent(courseId, 30)
                .ToDomainEvent()
                .WithTag("courseId", courseId.ToString()));

        await store.AppendAsync(
            new StudentEnrolledEvent(courseId, studentId)
                .ToDomainEvent()
                .WithTag("courseId", courseId.ToString())
                .WithTag("studentId", studentId.ToString()));

        var (courseExists, enrollmentCount, studentEnrolled, _) = await store.BuildDecisionModelAsync(
            CourseExistsProjection(courseId),
            EnrollmentCountProjection(courseId),
            studentExistsProjection);

        Assert.True(courseExists);
        Assert.Equal(1, enrollmentCount);
        Assert.True(studentEnrolled);
    }

    #endregion
}
