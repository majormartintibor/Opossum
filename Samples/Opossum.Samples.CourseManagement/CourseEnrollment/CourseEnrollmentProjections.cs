using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.Samples.CourseManagement.EnrollmentTier;
using Opossum.Samples.CourseManagement.Events;
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;

namespace Opossum.Samples.CourseManagement.CourseEnrollment;

/// <summary>
/// State produced by <see cref="CourseEnrollmentProjections.CourseCapacity"/>.
/// </summary>
/// <param name="MaxCapacity">Maximum number of students allowed.</param>
/// <param name="CurrentEnrollmentCount">Number of students currently enrolled.</param>
public sealed record CourseCapacityState(int MaxCapacity, int CurrentEnrollmentCount)
{
    /// <summary>True when no seats remain.</summary>
    public bool IsFull => CurrentEnrollmentCount >= MaxCapacity;
}

/// <summary>
/// State produced by <see cref="CourseEnrollmentProjections.StudentEnrollmentLimit"/>.
/// </summary>
/// <param name="Tier">The student's current subscription tier.</param>
/// <param name="CurrentCourseCount">Number of courses the student is currently enrolled in.</param>
public sealed record StudentEnrollmentLimitState(Tier Tier, int CurrentCourseCount)
{
    /// <summary>Maximum courses allowed for the student's tier.</summary>
    public int MaxAllowed => StudentMaxCourseEnrollment.GetMaxCoursesAllowed(Tier);

    /// <summary>True when the student has reached their enrollment limit.</summary>
    public bool IsAtLimit => CurrentCourseCount >= MaxAllowed;
}

/// <summary>
/// Factory methods for the three Decision Model projections used by
/// <see cref="EnrollStudentToCourseCommandHandler"/>.
/// Each projection is a single-purpose, in-memory, ephemeral fold — never persisted.
/// </summary>
public static class CourseEnrollmentProjections
{
    /// <summary>
    /// Tracks a course's capacity. Returns <see langword="null"/> until the course is created.
    /// </summary>
    public static IDecisionProjection<CourseCapacityState?> CourseCapacity(Guid courseId) =>
        new DecisionProjection<CourseCapacityState?>(
            initialState: null,
            query: Query.FromItems(new QueryItem
            {
                EventTypes =
                [
                    nameof(CourseCreatedEvent),
                    nameof(CourseStudentLimitModifiedEvent),
                    nameof(StudentEnrolledToCourseEvent)
                ],
                Tags = [new Tag("courseId", courseId.ToString())]
            }),
            apply: (state, evt) => evt.Event.Event switch
            {
                CourseCreatedEvent created =>
                    new CourseCapacityState(created.MaxStudentCount, 0),
                CourseStudentLimitModifiedEvent modified when state is not null =>
                    state with { MaxCapacity = modified.NewMaxStudentCount },
                StudentEnrolledToCourseEvent when state is not null =>
                    state with { CurrentEnrollmentCount = state.CurrentEnrollmentCount + 1 },
                _ => state
            });

    /// <summary>
    /// Tracks a student's enrollment tier and current course count.
    /// Returns <see langword="null"/> until the student is registered.
    /// </summary>
    public static IDecisionProjection<StudentEnrollmentLimitState?> StudentEnrollmentLimit(Guid studentId) =>
        new DecisionProjection<StudentEnrollmentLimitState?>(
            initialState: null,
            query: Query.FromItems(new QueryItem
            {
                EventTypes =
                [
                    nameof(StudentRegisteredEvent),
                    nameof(StudentSubscriptionUpdatedEvent),
                    nameof(StudentEnrolledToCourseEvent)
                ],
                Tags = [new Tag("studentId", studentId.ToString())]
            }),
            apply: (state, evt) => evt.Event.Event switch
            {
                StudentRegisteredEvent =>
                    new StudentEnrollmentLimitState(Tier.Basic, 0),
                StudentSubscriptionUpdatedEvent updated when state is not null =>
                    state with { Tier = updated.EnrollmentTier },
                StudentEnrolledToCourseEvent when state is not null =>
                    state with { CurrentCourseCount = state.CurrentCourseCount + 1 },
                _ => state
            });

    /// <summary>
    /// Detects whether a specific student is already enrolled in a specific course.
    /// The query requires BOTH tags (courseId AND studentId), so only the exact
    /// course-student pair triggers this projection.
    /// </summary>
    public static IDecisionProjection<bool> AlreadyEnrolled(Guid courseId, Guid studentId) =>
        new DecisionProjection<bool>(
            initialState: false,
            query: Query.FromItems(new QueryItem
            {
                EventTypes = [nameof(StudentEnrolledToCourseEvent)],
                Tags =
                [
                    new Tag("courseId", courseId.ToString()),
                    new Tag("studentId", studentId.ToString())
                ]
            }),
            apply: (_, _) => true);
}
