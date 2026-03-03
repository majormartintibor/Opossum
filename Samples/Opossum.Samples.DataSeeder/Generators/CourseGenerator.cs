using Opossum.Core;
using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.DataSeeder.Core;

namespace Opossum.Samples.DataSeeder.Generators;

/// <summary>
/// Generates <see cref="CourseCreatedEvent"/> records.
/// Distributes courses across three size categories (Small / Medium / Large) using the
/// percentages from <see cref="SeedingConfiguration"/>.
/// Populates <see cref="SeedContext.Courses"/> for downstream generators.
/// </summary>
public sealed class CourseGenerator : ISeedGenerator
{
    public IReadOnlyList<SeedEvent> Generate(SeedContext context, SeedingConfiguration config)
    {
        var random = context.Random;

        (string Size, int MinCap, int MaxCap, int Count)[] distribution =
        [
            ("Small",  10, 15, config.CourseCount * config.SmallCoursePercentage  / 100),
            ("Medium", 20, 30, config.CourseCount * config.MediumCoursePercentage / 100),
            ("Large",  40, 60, config.CourseCount * config.LargeCoursePercentage  / 100)
        ];

        var events = new List<SeedEvent>(config.CourseCount);

        foreach (var (size, minCap, maxCap, count) in distribution)
        {
            for (var i = 0; i < count; i++)
            {
                var courseId    = Guid.NewGuid();
                var name        = GeneratorHelper.GetCourseName(random, size);
                var capacity    = random.Next(minCap, maxCap + 1);
                var description = $"{size} course with capacity for {capacity} students.";

                context.Courses.Add(new CourseInfo(courseId, name, capacity));

                Tag[] tags = [new("courseId", courseId.ToString())];

                events.Add(GeneratorHelper.CreateSeedEvent(
                    new CourseCreatedEvent(courseId, name, description, capacity),
                    tags,
                    GeneratorHelper.RandomTimestamp(random, 365, 200)));
            }
        }

        return events;
    }
}
