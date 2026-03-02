using Opossum.Core;
using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.DataSeeder.Core;

namespace Opossum.Samples.DataSeeder.Generators;

/// <summary>
/// Generates <see cref="StudentEnrolledToCourseEvent"/> records.
/// Enforces all three enrollment invariants in pure code — no event-store reads required:
/// <list type="bullet">
///   <item>No duplicate (student, course) pair — tracked via <see cref="SeedContext.EnrolledPairs"/>.</item>
///   <item>Course not over capacity — tracked via <see cref="SeedContext.CourseEnrollmentCounts"/>.</item>
///   <item>Student not over tier limit — tracked via <see cref="SeedContext.StudentEnrollmentCounts"/>.</item>
/// </list>
/// Uses a partial Fisher-Yates shuffle over the available-course pool so that each
/// student receives a random, non-repeating set of courses in O(MaxCourses) time.
/// Full courses are swap-removed from the pool immediately, keeping all lookups O(1).
/// </summary>
public sealed class EnrollmentGenerator : ISeedGenerator
{
    public IReadOnlyList<SeedEvent> Generate(SeedContext context, SeedingConfiguration config)
    {
        var random = context.Random;
        var events = new List<SeedEvent>();

        foreach (var course in context.Courses)
            context.CourseEnrollmentCounts.TryAdd(course.CourseId, 0);

        foreach (var student in context.Students)
            context.StudentEnrollmentCounts.TryAdd(student.StudentId, 0);

        // Mutable working pool — full courses are swap-removed as they fill up.
        var available = new List<CourseInfo>(context.Courses);

        // Shuffle students so course slots are distributed evenly across tier groups.
        var students = context.Students.OrderBy(_ => random.Next()).ToList();

        foreach (var student in students)
        {
            if (available.Count == 0) break;

            var target   = Math.Min(student.MaxCourses, available.Count);
            var i        = 0;
            var enrolled = 0;

            while (enrolled < target && i < available.Count)
            {
                // Partial Fisher-Yates: swap a random remaining element into position i.
                var j = random.Next(i, available.Count);
                (available[i], available[j]) = (available[j], available[i]);

                var course = available[i];
                var pair   = (student.StudentId, course.CourseId);

                if (!context.EnrolledPairs.Contains(pair))
                {
                    Tag[] tags =
                    [
                        new("courseId",  course.CourseId.ToString()),
                        new("studentId", student.StudentId.ToString())
                    ];

                    events.Add(GeneratorHelper.CreateSeedEvent(
                        new StudentEnrolledToCourseEvent(course.CourseId, student.StudentId),
                        tags,
                        GeneratorHelper.RandomTimestamp(random, 120, 1)));

                    context.CourseEnrollmentCounts[course.CourseId]++;
                    context.StudentEnrollmentCounts[student.StudentId]++;
                    context.EnrolledPairs.Add(pair);
                    enrolled++;

                    // Swap-remove the course once it reaches capacity.
                    if (context.CourseEnrollmentCounts[course.CourseId] >= course.MaxCapacity)
                    {
                        available[i] = available[^1];
                        available.RemoveAt(available.Count - 1);
                        continue; // Don't advance i — a new element is now at position i.
                    }
                }

                i++;
            }
        }

        return events;
    }
}
