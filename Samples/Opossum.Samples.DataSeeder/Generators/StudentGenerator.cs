using Opossum.Core;
using Opossum.Samples.CourseManagement.EnrollmentTier;
using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.DataSeeder.Core;
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;

namespace Opossum.Samples.DataSeeder.Generators;

/// <summary>
/// Generates <see cref="StudentRegisteredEvent"/> records.
/// Enforces unique e-mail addresses and tier distribution matching the configured percentages.
/// Populates <see cref="SeedContext.Students"/> for downstream generators.
/// </summary>
public sealed class StudentGenerator : ISeedGenerator
{
    public IReadOnlyList<SeedEvent> Generate(SeedContext context, SeedingConfiguration config)
    {
        var random = context.Random;
        var events = new List<SeedEvent>(config.StudentCount);
        var usedEmails = new HashSet<string>(config.StudentCount);

        (Tier Tier, int Count)[] distribution =
        [
            (Tier.Basic,        config.StudentCount * config.BasicTierPercentage        / 100),
            (Tier.Standard,     config.StudentCount * config.StandardTierPercentage     / 100),
            (Tier.Professional, config.StudentCount * config.ProfessionalTierPercentage / 100),
            (Tier.Master,       config.StudentCount * config.MasterTierPercentage       / 100)
        ];

        foreach (var (tier, count) in distribution)
        {
            var maxCourses = StudentMaxCourseEnrollment.GetMaxCoursesAllowed(tier);

            for (var i = 0; i < count; i++)
            {
                var studentId = Guid.NewGuid();
                var firstName = GeneratorHelper.GetFirstName(random);
                var lastName  = GeneratorHelper.GetLastName(random);
                var email     = GenerateUniqueEmail(firstName, lastName, usedEmails);

                context.Students.Add(new StudentInfo(studentId, tier, maxCourses));

                Tag[] tags =
                [
                    new("studentEmail", email),
                    new("studentId",    studentId.ToString())
                ];

                events.Add(GeneratorHelper.CreateSeedEvent(
                    new StudentRegisteredEvent(studentId, firstName, lastName, email),
                    tags,
                    GeneratorHelper.RandomTimestamp(random, 365, 180)));
            }
        }

        return events;
    }

    private static string GenerateUniqueEmail(string firstName, string lastName, HashSet<string> used)
    {
        var baseEmail = $"{firstName.ToLower()}.{lastName.ToLower()}@privateschool.edu";
        if (used.Add(baseEmail)) return baseEmail;

        var counter = 2;
        while (true)
        {
            var email = $"{firstName.ToLower()}.{lastName.ToLower()}{counter}@privateschool.edu";
            if (used.Add(email)) return email;
            counter++;
        }
    }
}
