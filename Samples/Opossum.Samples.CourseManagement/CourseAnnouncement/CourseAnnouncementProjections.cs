using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.CourseAnnouncement;

/// <summary>
/// Decision Model projections for the Post Course Announcement command.
///
/// This feature showcases the DCB "Prevent Record Duplication" pattern:
/// https://dcb.events/examples/prevent-record-duplication/
///
/// The domain has no uniqueness constraint on announcements — a course can have
/// any number of them. The idempotency token is therefore the SOLE guard against
/// duplicate announcements caused by accidental HTTP retries. This is precisely
/// the infrastructure constraint the DCB spec demonstrates: enforcing "process
/// this request only once" without any domain-level backing.
///
/// Compare with <see cref="CourseEnrollment.CourseEnrollmentProjections.AlreadyEnrolled"/>,
/// which enforces a DOMAIN constraint (a student may only enroll once). Both use the
/// identical <c>(_, _) =&gt; true</c> projection shape — the key difference is what the
/// query is scoped to: a domain identity pair vs. a client-generated idempotency token.
/// </summary>
public static class CourseAnnouncementProjections
{
    /// <summary>
    /// Prerequisite: the course must exist before an announcement can be posted.
    /// Returns <see langword="true"/> once the first <see cref="CourseCreatedEvent"/> for
    /// <paramref name="courseId"/> is observed; <see langword="false"/> if the course has
    /// never been created.
    /// </summary>
    public static IDecisionProjection<bool> CourseExists(Guid courseId) =>
        new DecisionProjection<bool>(
            initialState: false,
            query: Query.FromItems(new QueryItem
            {
                EventTypes = [nameof(CourseCreatedEvent)],
                Tags = [new Tag("courseId", courseId.ToString())]
            }),
            apply: (_, _) => true);

    /// <summary>
    /// Idempotency guard: returns <see langword="true"/> when the given token has already
    /// been consumed by a <see cref="CourseAnnouncementPostedEvent"/>, and resets to
    /// <see langword="false"/> when the announcement is subsequently retracted via
    /// <see cref="CourseAnnouncementRetractedEvent"/> — freeing the token for reuse.
    ///
    /// The query is scoped exclusively to the <c>idempotency:{token}</c> tag.
    /// Two concurrent posts with DIFFERENT tokens produce entirely independent
    /// <see cref="AppendCondition"/> instances and never block each other.
    /// Two concurrent retries with the SAME token share one condition: only one append
    /// succeeds; the retry reads <c>tokenWasUsed = true</c> and returns the re-submission
    /// error — no special handling required.
    /// </summary>
    public static IDecisionProjection<bool> IdempotencyTokenWasUsed(Guid idempotencyToken) =>
        new DecisionProjection<bool>(
            initialState: false,
            query: Query.FromItems(new QueryItem
            {
                EventTypes =
                [
                    nameof(CourseAnnouncementPostedEvent),
                    nameof(CourseAnnouncementRetractedEvent)
                ],
                Tags = [new Tag("idempotency", idempotencyToken.ToString())]
            }),
            apply: (_, evt) => evt.Event.Event switch
            {
                CourseAnnouncementPostedEvent    => true,   // token consumed
                CourseAnnouncementRetractedEvent => false,  // token freed — may be reused
                _                               => false
            });
}
