namespace Opossum.Samples.DataSeeder.Core;

/// <summary>
/// Shared mutable state accumulator that generators read from and write to as they produce
/// seed events. Passed to each <see cref="ISeedGenerator"/> in dependency order so that
/// later generators can reference data produced by earlier ones.
/// </summary>
public sealed class SeedContext
{
    /// <summary>Seeded random instance shared across all generators for reproducibility.</summary>
    public Random Random { get; }

    /// <summary>Populated by <c>StudentGenerator</c>.</summary>
    public List<StudentInfo> Students { get; } = [];

    /// <summary>Populated by <c>CourseGenerator</c>.</summary>
    public List<CourseInfo> Courses { get; } = [];

    /// <summary>
    /// Tracks the number of students enrolled in each course.
    /// Populated and maintained by <c>EnrollmentGenerator</c>.
    /// </summary>
    public Dictionary<Guid, int> CourseEnrollmentCounts { get; } = new();

    /// <summary>
    /// Tracks the number of courses each student is enrolled in.
    /// Populated and maintained by <c>EnrollmentGenerator</c>.
    /// </summary>
    public Dictionary<Guid, int> StudentEnrollmentCounts { get; } = new();

    /// <summary>
    /// Prevents duplicate student-course enrollment pairs.
    /// Populated and maintained by <c>EnrollmentGenerator</c>.
    /// </summary>
    public HashSet<(Guid StudentId, Guid CourseId)> EnrolledPairs { get; } = new();

    /// <summary>Populated by <c>CourseBookGenerator</c>.</summary>
    public List<BookInfo> Books { get; } = [];

    /// <param name="randomSeed">Fixed seed for deterministic, reproducible generation. Default is 42.</param>
    public SeedContext(int randomSeed = 42)
    {
        Random = new Random(randomSeed);
    }
}
