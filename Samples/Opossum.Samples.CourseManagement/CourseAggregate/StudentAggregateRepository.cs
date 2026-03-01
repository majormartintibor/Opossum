using Opossum.Core;
using Opossum.Extensions;

namespace Opossum.Samples.CourseManagement.CourseAggregate;

/// <summary>
/// Repository for <see cref="StudentAggregate"/> that reads events from the
/// Opossum event store filtered by the <c>studentId</c> tag.
/// </summary>
/// <remarks>
/// <para>
/// This repository is for <em>standalone</em> student aggregate reads — when only
/// student-centric state is needed (e.g. a dedicated student-aggregate endpoint).
/// </para>
/// <para>
/// <b>Do not use this for the subscription flow.</b><br/>
/// When cross-aggregate atomicity is required — i.e. when both
/// <see cref="CourseAggregate"/> and <see cref="StudentAggregate"/> must be checked
/// together — use <see cref="CourseAggregateRepository.LoadBothForSubscriptionAsync"/>
/// instead. That method performs a <em>single</em> unified read over both entity tag
/// queries, producing a shared <c>AfterSequencePosition</c> watermark that correctly
/// guards the compound <see cref="AppendCondition"/>. Two separate calls to
/// <see cref="LoadAsync"/> and <see cref="CourseAggregateRepository.LoadAsync"/>
/// would leave a race window between the reads.
/// </para>
/// </remarks>
public sealed class StudentAggregateRepository(IEventStore eventStore)
{
    /// <summary>
    /// Loads the student aggregate by replaying all events tagged with
    /// <paramref name="studentId"/>. Returns <see langword="null"/> when no events
    /// exist for that student.
    /// </summary>
    public async Task<StudentAggregate?> LoadAsync(Guid studentId)
    {
        var query = Query.FromTags(new Tag("studentId", studentId.ToString()));
        var events = await eventStore.ReadAsync(query);

        if (events.Length == 0)
            return null;

        return StudentAggregate.Reconstitute(events);
    }
}
