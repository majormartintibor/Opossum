using Opossum.Core;
using Opossum.Samples.DataSeeder.Core;

namespace Opossum.Samples.DataSeeder.Generators;

/// <summary>
/// Shared name data and utility methods used by all seed generators.
/// </summary>
internal static class GeneratorHelper
{
    private static readonly string[] _firstNames =
    [
        "Emma", "Liam", "Olivia", "Noah", "Ava", "Ethan", "Sophia", "Mason",
        "Isabella", "William", "Mia", "James", "Charlotte", "Benjamin", "Amelia", "Lucas",
        "Harper", "Henry", "Evelyn", "Alexander", "Abigail", "Michael", "Emily", "Daniel",
        "Elizabeth", "Matthew", "Sofia", "Jackson", "Avery", "Sebastian", "Ella", "David",
        "Scarlett", "Joseph", "Grace", "Carter", "Chloe", "Owen", "Victoria", "Wyatt",
        "Riley", "John", "Aria", "Jack", "Lily", "Luke", "Aubrey", "Jayden", "Zoey"
    ];

    private static readonly string[] _lastNames =
    [
        "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller",
        "Davis", "Rodriguez", "Martinez", "Hernandez", "Lopez", "Gonzalez", "Wilson",
        "Anderson", "Thomas", "Taylor", "Moore", "Jackson", "Martin", "Lee", "Perez",
        "Thompson", "White", "Harris", "Sanchez", "Clark", "Ramirez", "Lewis", "Robinson"
    ];

    private static readonly string[] _smallCourseNames =
    [
        "Advanced Poetry", "Latin Prose", "Music Theory", "Studio Art", "Philosophy Seminar",
        "Creative Writing", "Ethics Debate", "Ancient Greek", "Jazz Ensemble", "Shakespeare Studies"
    ];

    private static readonly string[] _mediumCourseNames =
    [
        "World History", "Chemistry", "Algebra II", "English Literature", "Biology",
        "Spanish I", "Physics", "U.S. History", "Geometry", "French II", "Psychology", "Economics"
    ];

    private static readonly string[] _largeCourseNames =
    [
        "Introduction to Computer Science", "Physical Education", "Health & Wellness",
        "Public Speaking", "College Prep Math", "SAT Preparation"
    ];

    internal static string GetFirstName(Random random) =>
        _firstNames[random.Next(_firstNames.Length)];

    internal static string GetLastName(Random random) =>
        _lastNames[random.Next(_lastNames.Length)];

    internal static string GetCourseName(Random random, string sizeCategory)
    {
        var subjects = sizeCategory switch
        {
            "Small"  => _smallCourseNames,
            "Medium" => _mediumCourseNames,
            "Large"  => _largeCourseNames,
            _        => _mediumCourseNames
        };
        var subject = subjects[random.Next(subjects.Length)];
        return sizeCategory == "Large" ? subject : $"{subject} - Level {random.Next(1, 4)}";
    }

    /// <summary>
    /// Returns a random <see cref="DateTimeOffset"/> between <paramref name="daysAgoMin"/>
    /// and <paramref name="daysAgoMax"/> days in the past (UTC).
    /// </summary>
    internal static DateTimeOffset RandomTimestamp(Random random, int daysAgoMax, int daysAgoMin)
    {
        var days = random.Next(daysAgoMin, daysAgoMax + 1);
        return DateTimeOffset.UtcNow.AddDays(-days);
    }

    /// <summary>
    /// Constructs a <see cref="SeedEvent"/> from a payload, tag array, and timestamp.
    /// </summary>
    internal static SeedEvent CreateSeedEvent(IEvent payload, Tag[] tags, DateTimeOffset timestamp) =>
        new(
            new DomainEvent { Event = payload, Tags = tags },
            new Metadata { Timestamp = timestamp }
        );
}
