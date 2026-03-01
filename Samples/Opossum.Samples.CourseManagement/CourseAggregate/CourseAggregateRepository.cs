using Opossum.Core;
using Opossum.Extensions;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.CourseAggregate;

/// <summary>
/// Repository for <see cref="CourseAggregate"/> that uses Opossum's tag-based event store
/// as the DCB example at <see href="https://dcb.events/examples/event-sourced-aggregate/#dcb-approach"/>
/// prescribes: a tag-scoped query replaces the traditional per-stream version lock.
/// </summary>
/// <remarks>
/// <para><b>Load:</b> reads all events tagged <c>courseId=&lt;id&gt;</c> from the store,
/// feeds them to <see cref="CourseAggregate.Reconstitute"/>, and returns <see langword="null"/>
/// when no events exist yet for that course.</para>
///
/// <para><b>Save:</b> appends all recorded events atomically with an
/// <see cref="AppendCondition"/> that encodes the DCB optimistic lock:</para>
/// <list type="bullet">
///   <item>
///     <description>
///       <c>FailIfEventsMatch</c> — the same <c>courseId</c> tag query used to load the aggregate.
///       Any write to this course between our read and our append will be detected.
///     </description>
///   </item>
///   <item>
///     <description>
///       <c>AfterSequencePosition = aggregate.Version</c> — only events appended
///       <em>after</em> our last read position can invalidate the decision.
///       When <c>Version == 0</c> (new aggregate), <see langword="null"/> is used instead,
///       which rejects the save if <em>any</em> course event already exists — preventing
///       duplicate creation.
///     </description>
///   </item>
/// </list>
/// <para>
/// On conflict, <see cref="SaveAsync"/> throws
/// <see cref="Opossum.Exceptions.AppendConditionFailedException"/>.
/// The caller is responsible for catching it, reloading via <see cref="LoadAsync"/>,
/// and retrying the command. See <c>CourseAggregateEndpoints</c> for the retry pattern.
/// </para>
/// </remarks>
public sealed class CourseAggregateRepository(IEventStore eventStore)
{
    /// <summary>
    /// Loads the aggregate by replaying all events tagged with <paramref name="courseId"/>.
    /// Returns <see langword="null"/> when no events exist for that course.
    /// </summary>
    public async Task<CourseAggregate?> LoadAsync(Guid courseId)
    {
        var query = Query.FromTags(new Tag("courseId", courseId.ToString()));
        var events = await eventStore.ReadAsync(query);

        if (events.Length == 0)
            return null;

        return CourseAggregate.Reconstitute(events);
    }

    /// <summary>
    /// Atomically appends all events recorded since <see cref="CourseAggregate.Create"/>
    /// or the last successful <see cref="SaveAsync"/> call, guarded by the DCB
    /// optimistic-concurrency condition.
    /// </summary>
    /// <exception cref="Opossum.Exceptions.AppendConditionFailedException">
    /// Thrown when a concurrent write invalidated the aggregate's version.
    /// Reload the aggregate with <see cref="LoadAsync"/> and retry the command.
    /// </exception>
    public async Task SaveAsync(CourseAggregate aggregate, CancellationToken cancellationToken = default)
    {
        var recordedEvents = aggregate.PullRecordedEvents();
        if (recordedEvents.Length == 0)
            return;

        // The same query used to load the aggregate — this is the DCB consistency boundary.
        var query = Query.FromTags(new Tag("courseId", aggregate.CourseId.ToString()));

        var condition = new AppendCondition
        {
            FailIfEventsMatch = query,
            // null when Version == 0 (new aggregate): reject if ANY course event already exists.
            // Otherwise: reject only if a new event was appended after our last read.
            AfterSequencePosition = aggregate.Version == 0 ? null : aggregate.Version
        };

        var newEvents = recordedEvents
            .Select(e =>
            {
                var builder = e.ToDomainEvent()
                    .WithTag("courseId", aggregate.CourseId.ToString())
                    .WithTimestamp(DateTimeOffset.UtcNow);

                // StudentEnrolledToCourseEvent is indexed by studentId by the read-side projections
                // (e.g. StudentShortInfoProjection) — add the same tag the Decision Model endpoint does.
                if (e is StudentEnrolledToCourseEvent enrolled)
                    builder = builder.WithTag("studentId", enrolled.StudentId.ToString());

                return (NewEvent)builder;
            })
            .ToArray();

        await eventStore.AppendAsync(newEvents, condition, cancellationToken);
    }
}
