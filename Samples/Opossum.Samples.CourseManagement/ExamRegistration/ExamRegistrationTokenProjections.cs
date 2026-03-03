using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.ExamRegistration;

/// <summary>
/// Lifecycle status of a server-generated exam registration token.
/// </summary>
public enum ExamTokenStatus
{
    /// <summary>No token with this id has ever been issued.</summary>
    NotIssued,

    /// <summary>Token was issued and is available for redemption.</summary>
    Issued,

    /// <summary>Token was revoked by the instructor before it was redeemed.</summary>
    Revoked,

    /// <summary>Token was successfully redeemed by a student.</summary>
    Redeemed
}

/// <summary>
/// State produced by <see cref="ExamRegistrationTokenProjections.TokenStatus"/>.
/// Carries the lifecycle status alongside the <see cref="ExamId"/> from the issued event,
/// so command handlers can construct outgoing events without an extra store read.
/// </summary>
/// <param name="Status">Current lifecycle status of the token.</param>
/// <param name="ExamId">The exam this token belongs to. <see cref="Guid.Empty"/> when <see cref="Status"/> is <see cref="ExamTokenStatus.NotIssued"/>.</param>
public sealed record ExamTokenState(ExamTokenStatus Status, Guid ExamId);

/// <summary>
/// Decision Model projections for the Exam Registration Token feature.
///
/// This feature showcases the DCB "Opt-In Token" pattern:
/// https://dcb.events/examples/opt-in-token/
///
/// The key teaching point: DCB replaces a persistent "valid tokens" read model entirely.
/// No <c>IProjectionDefinition</c> for token state is needed for correctness — the event
/// store query scoped to <c>examToken:{tokenId}</c> IS the token registry.
///
/// A single <see cref="ExamTokenStatus"/> enum projection replaces the two-bool pattern
/// (WasIssued + WasRedeemed) and naturally accommodates revocation as a third state.
/// </summary>
public static class ExamRegistrationTokenProjections
{
    /// <summary>
    /// Returns the current lifecycle state of the exam registration token identified by
    /// <paramref name="tokenId"/>.
    ///
    /// Query is scoped exclusively to the <c>examToken:{tokenId}</c> tag — different tokens
    /// never contend. Two concurrent redemptions of the same token: one succeeds; the other
    /// reads <c>Redeemed</c> on retry and returns the appropriate error.
    /// </summary>
    public static IDecisionProjection<ExamTokenState> TokenStatus(Guid tokenId) =>
        new DecisionProjection<ExamTokenState>(
            initialState: new ExamTokenState(ExamTokenStatus.NotIssued, Guid.Empty),
            query: Query.FromItems(new QueryItem
            {
                EventTypes =
                [
                    nameof(ExamRegistrationTokenIssuedEvent),
                    nameof(ExamRegistrationTokenRevokedEvent),
                    nameof(ExamRegistrationTokenRedeemedEvent)
                ],
                Tags = [new Tag("examToken", tokenId.ToString())]
            }),
            apply: (state, evt) => evt.Event.Event switch
            {
                ExamRegistrationTokenIssuedEvent issued   => new ExamTokenState(ExamTokenStatus.Issued, issued.ExamId),
                ExamRegistrationTokenRevokedEvent         => state with { Status = ExamTokenStatus.Revoked },
                ExamRegistrationTokenRedeemedEvent        => state with { Status = ExamTokenStatus.Redeemed },
                _                                        => state
            });

    /// <summary>
    /// Prerequisite guard: the course referenced by the token must exist.
    /// Returns <see langword="true"/> once the first <see cref="CourseCreatedEvent"/>
    /// for <paramref name="courseId"/> is observed.
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
}
