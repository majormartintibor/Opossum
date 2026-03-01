using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.CourseAnnouncementRetraction;

/// <summary>
/// Tracks whether an announcement (identified by its idempotency token) can still be
/// retracted, and — crucially — whether its token has been freed for reuse.
/// </summary>
/// <param name="AnnouncementId">The server-assigned announcement identifier.</param>
/// <param name="CourseId">The course the announcement belongs to.</param>
/// <param name="IsRetracted">
/// <see langword="true"/> once a <see cref="CourseAnnouncementRetractedEvent"/> has been
/// appended for this token.
/// </param>
public sealed record RetractableAnnouncementState(Guid AnnouncementId, Guid CourseId, bool IsRetracted);

/// <summary>
/// Decision Model projection for the Retract Course Announcement command.
///
/// Folds both <see cref="CourseAnnouncementPostedEvent"/> and
/// <see cref="CourseAnnouncementRetractedEvent"/> scoped to a single idempotency token.
/// When applied in sequence-order the final state correctly reflects:
/// <list type="bullet">
///   <item><description><see langword="null"/> — no announcement with this token exists.</description></item>
///   <item><description><c>IsRetracted = false</c> — announcement exists and can be retracted.</description></item>
///   <item><description><c>IsRetracted = true</c> — already retracted; the token has been freed.</description></item>
/// </list>
///
/// The same query used here is mirrored inside
/// <see cref="CourseAnnouncement.CourseAnnouncementProjections.IdempotencyTokenWasUsed"/>.
/// After a retraction both projections converge on <see langword="false"/> / freed-state —
/// the token reuse is a consequence of the event fold, not a special case.
/// </summary>
public static class CourseAnnouncementRetractionProjection
{
    public static IDecisionProjection<RetractableAnnouncementState?> RetractableAnnouncement(
        Guid idempotencyToken) =>
        new DecisionProjection<RetractableAnnouncementState?>(
            initialState: null,
            query: Query.FromItems(new QueryItem
            {
                EventTypes =
                [
                    nameof(CourseAnnouncementPostedEvent),
                    nameof(CourseAnnouncementRetractedEvent)
                ],
                Tags = [new Tag("idempotency", idempotencyToken.ToString())]
            }),
            apply: (state, evt) => evt.Event.Event switch
            {
                CourseAnnouncementPostedEvent posted =>
                    new RetractableAnnouncementState(posted.AnnouncementId, posted.CourseId, IsRetracted: false),
                CourseAnnouncementRetractedEvent when state is not null =>
                    state with { IsRetracted = true },
                _ => state
            });
}
