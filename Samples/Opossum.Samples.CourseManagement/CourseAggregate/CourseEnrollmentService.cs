using Opossum.Core;

namespace Opossum.Samples.CourseManagement.CourseAggregate;

/// <summary>
/// Domain service that coordinates the enrollment of a student into a course,
/// enforcing all three cross-aggregate invariants using the Event-Sourced Aggregate pattern.
/// </summary>
/// <remarks>
/// <para>
/// Enrollment touches two aggregates: <see cref="CourseAggregate"/> owns course capacity
/// and duplicate-enrollment detection; <see cref="StudentAggregate"/> owns the tier-based
/// enrollment limit. Neither aggregate alone can enforce the complete rule, so a domain
/// service is the correct DDD construct for the coordination.
/// </para>
/// <para>
/// <b>Two independent reads — why this is safe</b><br/>
/// Each aggregate is loaded via its own repository in two sequential calls. This is safe
/// because store positions are <em>globally</em> monotonically increasing across all event
/// types. If <c>course.Version = 100</c> (the last course event sat at global position 100)
/// and <c>student.Version = 50</c>, positions 51–100 are already occupied by course events
/// — no student event can appear in that range retrospectively. Any event appended to
/// <em>either</em> entity <em>after</em> our reads will therefore have a position
/// strictly greater than <c>MAX(100, 50) = 100</c>.
/// The compound <see cref="AppendCondition"/> uses exactly that MAX value as its
/// <see cref="AppendCondition.AfterSequencePosition"/> watermark, so concurrent writes
/// to either entity are always detected.
/// </para>
/// <para>
/// <b>Compare with the DCB approach</b><br/>
/// <c>EnrollStudentToCourseCommand</c> achieves the same guarantee with a single
/// <c>BuildDecisionModelAsync</c> call — the unified read and compound condition are
/// produced automatically from the projection queries. The domain-service shape here
/// is architecturally cleaner than mixing concerns in a repository, but it is still
/// significantly more code than the DCB alternative. See
/// <c>docs/analysis/aggregate-vs-dcb-comparison.md</c> for the full comparison.
/// </para>
/// </remarks>
public sealed class CourseEnrollmentService(
    CourseAggregateRepository courseRepository,
    StudentAggregateRepository studentRepository)
{
    /// <summary>
    /// Enrolls <paramref name="studentId"/> in <paramref name="courseId"/>, enforcing
    /// course capacity, student tier limit, and duplicate enrollment atomically.
    /// </summary>
    /// <returns>
    /// <see cref="CommandResult.Ok()"/> on success; <see cref="CommandResult.Fail"/>
    /// for business-rule violations (course not found, student not registered, at limit,
    /// already enrolled, course full).
    /// </returns>
    /// <exception cref="Opossum.Exceptions.AppendConditionFailedException">
    /// Propagated from <see cref="CourseAggregateRepository.SaveAsync"/> when a concurrent
    /// write to either entity was detected between the reads and the append. The caller
    /// is responsible for reloading and retrying — see <c>CourseAggregateEndpoints</c>.
    /// </exception>
    public async Task<CommandResult> EnrollStudentAsync(
        Guid courseId,
        Guid studentId,
        CancellationToken cancellationToken = default)
    {
        var course = await courseRepository.LoadAsync(courseId);
        if (course is null)
            return CommandResult.Fail($"Course '{courseId}' does not exist.");

        var student = await studentRepository.LoadAsync(studentId);
        if (student is null)
            return CommandResult.Fail($"Student '{studentId}' is not registered.");

        if (student.IsAtEnrollmentLimit)
            return CommandResult.Fail(
                $"Student has reached their enrollment limit " +
                $"({student.MaxCoursesAllowed} courses for {student.EnrollmentTier} tier).");

        try
        {
            // CourseAggregate enforces course capacity and duplicate enrollment.
            course.SubscribeStudent(studentId);
        }
        catch (InvalidOperationException ex)
        {
            return CommandResult.Fail(ex.Message);
        }

        // Compound AppendCondition spanning BOTH entity tag queries.
        //
        // AfterSequencePosition = MAX(course.Version, student.Version).
        // This is the safe watermark derived from two independent reads: because positions
        // are globally monotonically increasing, any event appended after either read must
        // have a position > MAX, so the OR-compound guard below will always detect it.
        var compoundCondition = new AppendCondition
        {
            FailIfEventsMatch = Query.FromItems(
                new QueryItem { Tags = [new Tag("courseId", courseId.ToString())] },
                new QueryItem { Tags = [new Tag("studentId", studentId.ToString())] }),
            AfterSequencePosition = Math.Max(course.Version, student.Version)
        };

        await courseRepository.SaveAsync(course, compoundCondition, cancellationToken);
        return CommandResult.Ok();
    }
}
