using Opossum.Core;
using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.DataSeeder.Core;

namespace Opossum.Samples.DataSeeder.Generators;

/// <summary>
/// Generates exam registration token lifecycle events:
/// <see cref="ExamRegistrationTokenIssuedEvent"/>,
/// <see cref="ExamRegistrationTokenRedeemedEvent"/>, and
/// <see cref="ExamRegistrationTokenRevokedEvent"/>.
/// <para>
/// Invariants enforced in pure code:
/// <list type="bullet">
///   <item>Each token has a unique <c>TokenId</c>.</item>
///   <item>Redeemed and revoked events are mutually exclusive per token.</item>
///   <item>The student in a redemption event is enrolled in the token's course.</item>
///   <item>Issued timestamp precedes redeemed / revoked timestamps.</item>
/// </list>
/// </para>
/// Lifecycle distribution: ~<see cref="SeedingConfiguration.TokenRedemptionPercentage"/>%
/// redeemed, ~<see cref="SeedingConfiguration.TokenRevocationPercentage"/>% revoked,
/// remainder open.
/// </summary>
public sealed class ExamTokenGenerator : ISeedGenerator
{
    public IReadOnlyList<SeedEvent> Generate(SeedContext context, SeedingConfiguration config)
    {
        if (config.ExamsPerCourse <= 0 || config.TokensPerExam <= 0 || context.Courses.Count == 0)
            return [];

        var random = context.Random;
        var events = new List<SeedEvent>(
            context.Courses.Count * config.ExamsPerCourse * config.TokensPerExam * 2);

        // Build course → enrolled student list from the EnrolledPairs set.
        var courseStudents = new Dictionary<Guid, List<Guid>>(context.Courses.Count);
        foreach (var (studentId, courseId) in context.EnrolledPairs)
        {
            if (!courseStudents.TryGetValue(courseId, out var list))
            {
                list = [];
                courseStudents[courseId] = list;
            }
            list.Add(studentId);
        }

        foreach (var course in context.Courses)
        {
            courseStudents.TryGetValue(course.CourseId, out var enrolledStudents);

            for (var e = 0; e < config.ExamsPerCourse; e++)
            {
                var examId = Guid.NewGuid();

                for (var t = 0; t < config.TokensPerExam; t++)
                {
                    var tokenId  = Guid.NewGuid();
                    var issuedAt = GeneratorHelper.RandomTimestamp(random, 60, 14);

                    Tag[] issuedTags =
                    [
                        new("examToken", tokenId.ToString()),
                        new("examId",    examId.ToString()),
                        new("courseId",  course.CourseId.ToString())
                    ];

                    events.Add(GeneratorHelper.CreateSeedEvent(
                        new ExamRegistrationTokenIssuedEvent(tokenId, examId, course.CourseId),
                        issuedTags,
                        issuedAt));

                    var roll = random.Next(100);

                    if (roll < config.TokenRedemptionPercentage && enrolledStudents is { Count: > 0 })
                    {
                        var studentId  = enrolledStudents[random.Next(enrolledStudents.Count)];
                        var redeemedAt = issuedAt.AddDays(random.Next(1, 6));

                        Tag[] redeemedTags =
                        [
                            new("examToken", tokenId.ToString()),
                            new("examId",    examId.ToString()),
                            new("studentId", studentId.ToString())
                        ];

                        events.Add(GeneratorHelper.CreateSeedEvent(
                            new ExamRegistrationTokenRedeemedEvent(tokenId, examId, studentId),
                            redeemedTags,
                            redeemedAt));
                    }
                    else if (roll < config.TokenRedemptionPercentage + config.TokenRevocationPercentage)
                    {
                        var revokedAt = issuedAt.AddDays(random.Next(1, 4));

                        Tag[] revokedTags =
                        [
                            new("examToken", tokenId.ToString()),
                            new("examId",    examId.ToString())
                        ];

                        events.Add(GeneratorHelper.CreateSeedEvent(
                            new ExamRegistrationTokenRevokedEvent(tokenId, examId),
                            revokedTags,
                            revokedAt));
                    }
                    // else: token remains open — no further events
                }
            }
        }

        return events;
    }
}
