using Opossum.Core;
using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.DataSeeder.Core;

namespace Opossum.Samples.DataSeeder.Generators;

/// <summary>
/// Generates <see cref="CourseAnnouncementPostedEvent"/> and optionally
/// <see cref="CourseAnnouncementRetractedEvent"/> records.
/// Enforces a unique <c>AnnouncementId</c> and a unique <c>IdempotencyToken</c> per
/// announcement; retraction events reuse the same token as the corresponding posted event.
/// </summary>
public sealed class AnnouncementGenerator : ISeedGenerator
{
    private static readonly string[] _titles =
    [
        "Exam Dates Updated", "New Study Materials Available", "Office Hours Changed",
        "Assignment Deadline Extended", "Guest Lecture Announced", "Course Schedule Revised",
        "Important Policy Update", "Practice Exams Now Available", "Lab Session Rescheduled",
        "Mid-Term Review Session Added"
    ];

    private static readonly string[] _bodies =
    [
        "Please review the updated schedule on the course portal.",
        "New materials have been added to the course resources section.",
        "Check your email for the updated calendar invite.",
        "The assignment deadline has been extended by one week.",
        "A renowned expert will be giving a guest lecture next week.",
        "Please update your schedules accordingly.",
        "Review the updated course policy document for details.",
        "Practice exams are now available in the course portal.",
        "The rescheduled session details have been posted on the portal.",
        "A review session has been added to help prepare for the mid-term."
    ];

    public IReadOnlyList<SeedEvent> Generate(SeedContext context, SeedingConfiguration config)
    {
        if (config.AnnouncementsPerCourse <= 0 || context.Courses.Count == 0) return [];

        var random = context.Random;
        var events = new List<SeedEvent>(context.Courses.Count * config.AnnouncementsPerCourse);

        foreach (var course in context.Courses)
        {
            for (var i = 0; i < config.AnnouncementsPerCourse; i++)
            {
                var announcementId   = Guid.NewGuid();
                var idempotencyToken = Guid.NewGuid();
                var title            = _titles[random.Next(_titles.Length)];
                var body             = _bodies[random.Next(_bodies.Length)];
                var postedAt         = GeneratorHelper.RandomTimestamp(random, 90, 30);

                Tag[] postedTags =
                [
                    new("courseId",    course.CourseId.ToString()),
                    new("idempotency", idempotencyToken.ToString())
                ];

                events.Add(GeneratorHelper.CreateSeedEvent(
                    new CourseAnnouncementPostedEvent(announcementId, course.CourseId, title, body, idempotencyToken),
                    postedTags,
                    postedAt));

                if (random.Next(100) < config.AnnouncementRetractionPercentage)
                {
                    var retractedAt = postedAt.AddDays(random.Next(1, 8));

                    Tag[] retractedTags =
                    [
                        new("courseId",    course.CourseId.ToString()),
                        new("idempotency", idempotencyToken.ToString())
                    ];

                    events.Add(GeneratorHelper.CreateSeedEvent(
                        new CourseAnnouncementRetractedEvent(announcementId, course.CourseId, idempotencyToken),
                        retractedTags,
                        retractedAt));
                }
            }
        }

        return events;
    }
}
