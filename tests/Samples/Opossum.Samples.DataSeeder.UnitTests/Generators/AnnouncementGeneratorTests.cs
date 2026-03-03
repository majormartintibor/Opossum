using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.DataSeeder;
using Opossum.Samples.DataSeeder.Core;
using Opossum.Samples.DataSeeder.Generators;

namespace Opossum.Samples.DataSeeder.UnitTests.Generators;

/// <summary>
/// Unit tests for <see cref="AnnouncementGenerator"/>.
/// All assertions operate on pure in-memory output — no I/O.
/// </summary>
public sealed class AnnouncementGeneratorTests
{
    private static SeedingConfiguration DefaultConfig => new()
    {
        CourseCount                  = 5,
        SmallCoursePercentage        = 100,
        MediumCoursePercentage       = 0,
        LargeCoursePercentage        = 0,
        AnnouncementsPerCourse       = 3,
        AnnouncementRetractionPercentage = 20
    };

    private static SeedContext BuildContext()
    {
        var context = new SeedContext(randomSeed: 42);
        new CourseGenerator().Generate(context, DefaultConfig);
        return context;
    }

    private readonly AnnouncementGenerator _sut = new();

    [Fact]
    public void Generate_ProducesAtLeastAnnouncementsPerCoursePostedEvents()
    {
        var context = BuildContext();
        var events  = _sut.Generate(context, DefaultConfig);

        var postedCount = events.Count(e => e.Event.Event is CourseAnnouncementPostedEvent);

        Assert.Equal(DefaultConfig.CourseCount * DefaultConfig.AnnouncementsPerCourse, postedCount);
    }

    [Fact]
    public void Generate_EventPayloadsArePostedOrRetractedEvents()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        Assert.All(events, e => Assert.True(
            e.Event.Event is CourseAnnouncementPostedEvent
            || e.Event.Event is CourseAnnouncementRetractedEvent));
    }

    [Fact]
    public void Generate_PostedEventAnnouncementIdsAreUnique()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var ids = events
            .Where(e => e.Event.Event is CourseAnnouncementPostedEvent)
            .Select(e => ((CourseAnnouncementPostedEvent)e.Event.Event).AnnouncementId)
            .ToList();

        Assert.Equal(ids.Distinct().Count(), ids.Count);
    }

    [Fact]
    public void Generate_IdempotencyTokensAreUniqueAcrossPostedEvents()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var tokens = events
            .Where(e => e.Event.Event is CourseAnnouncementPostedEvent)
            .Select(e => ((CourseAnnouncementPostedEvent)e.Event.Event).IdempotencyToken)
            .ToList();

        Assert.Equal(tokens.Distinct().Count(), tokens.Count);
    }

    [Fact]
    public void Generate_RetractedEventUsesTheSameIdempotencyTokenAsPostedEvent()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var postedByAnnouncement = events
            .Where(e => e.Event.Event is CourseAnnouncementPostedEvent)
            .ToDictionary(
                e => ((CourseAnnouncementPostedEvent)e.Event.Event).AnnouncementId,
                e => ((CourseAnnouncementPostedEvent)e.Event.Event).IdempotencyToken);

        var retractedEvents = events
            .Where(e => e.Event.Event is CourseAnnouncementRetractedEvent)
            .ToList();

        Assert.All(retractedEvents, e =>
        {
            var retracted = (CourseAnnouncementRetractedEvent)e.Event.Event;
            Assert.True(postedByAnnouncement.TryGetValue(retracted.AnnouncementId, out var postedToken));
            Assert.Equal(postedToken, retracted.IdempotencyToken);
        });
    }

    [Fact]
    public void Generate_RetractedTimestampIsAfterPostedTimestamp()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var postedTimestamps = events
            .Where(e => e.Event.Event is CourseAnnouncementPostedEvent)
            .ToDictionary(
                e => ((CourseAnnouncementPostedEvent)e.Event.Event).AnnouncementId,
                e => e.Metadata.Timestamp);

        var retractedEvents = events
            .Where(e => e.Event.Event is CourseAnnouncementRetractedEvent)
            .ToList();

        Assert.All(retractedEvents, e =>
        {
            var retracted = (CourseAnnouncementRetractedEvent)e.Event.Event;
            Assert.True(postedTimestamps.TryGetValue(retracted.AnnouncementId, out var postedAt));
            Assert.True(e.Metadata.Timestamp > postedAt);
        });
    }

    [Fact]
    public void Generate_EachPostedEventHasCourseIdTag()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var postedEvents = events.Where(e => e.Event.Event is CourseAnnouncementPostedEvent);

        Assert.All(postedEvents, e => Assert.Contains(e.Event.Tags, t => t.Key == "courseId"));
    }

    [Fact]
    public void Generate_EachPostedEventHasIdempotencyTag()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var postedEvents = events.Where(e => e.Event.Event is CourseAnnouncementPostedEvent);

        Assert.All(postedEvents, e => Assert.Contains(e.Event.Tags, t => t.Key == "idempotency"));
    }

    [Fact]
    public void Generate_EachRetractedEventHasCourseIdTag()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var retractedEvents = events.Where(e => e.Event.Event is CourseAnnouncementRetractedEvent);

        Assert.All(retractedEvents, e => Assert.Contains(e.Event.Tags, t => t.Key == "courseId"));
    }

    [Fact]
    public void Generate_EachRetractedEventHasIdempotencyTag()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var retractedEvents = events.Where(e => e.Event.Event is CourseAnnouncementRetractedEvent);

        Assert.All(retractedEvents, e => Assert.Contains(e.Event.Tags, t => t.Key == "idempotency"));
    }

    [Fact]
    public void Generate_ReturnsEmptyWhenAnnouncementsPerCourseIsZero()
    {
        var config = new SeedingConfiguration
        {
            CourseCount                  = DefaultConfig.CourseCount,
            SmallCoursePercentage        = DefaultConfig.SmallCoursePercentage,
            MediumCoursePercentage       = DefaultConfig.MediumCoursePercentage,
            LargeCoursePercentage        = DefaultConfig.LargeCoursePercentage,
            AnnouncementsPerCourse       = 0,
            AnnouncementRetractionPercentage = DefaultConfig.AnnouncementRetractionPercentage
        };

        var events = _sut.Generate(BuildContext(), config);

        Assert.Empty(events);
    }

    [Fact]
    public void Generate_ReturnsEmptyWhenNoCoursesExist()
    {
        var events = _sut.Generate(new SeedContext(randomSeed: 42), DefaultConfig);

        Assert.Empty(events);
    }

    [Fact]
    public void Generate_CourseIdTagMatchesEventPayloadCourseId()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var postedEvents = events.Where(e => e.Event.Event is CourseAnnouncementPostedEvent);

        Assert.All(postedEvents, e =>
        {
            var posted    = (CourseAnnouncementPostedEvent)e.Event.Event;
            var courseTag = e.Event.Tags.First(t => t.Key == "courseId").Value;
            Assert.Equal(posted.CourseId.ToString(), courseTag);
        });
    }

    [Fact]
    public void Generate_IdempotencyTagMatchesEventPayloadToken()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var postedEvents = events.Where(e => e.Event.Event is CourseAnnouncementPostedEvent);

        Assert.All(postedEvents, e =>
        {
            var posted       = (CourseAnnouncementPostedEvent)e.Event.Event;
            var idempotency  = e.Event.Tags.First(t => t.Key == "idempotency").Value;
            Assert.Equal(posted.IdempotencyToken.ToString(), idempotency);
        });
    }

    [Fact]
    public void Generate_WithHighRetractionPercentage_ProducesRetractedEvents()
    {
        var config  = new SeedingConfiguration
        {
            CourseCount                      = DefaultConfig.CourseCount,
            SmallCoursePercentage            = DefaultConfig.SmallCoursePercentage,
            MediumCoursePercentage           = DefaultConfig.MediumCoursePercentage,
            LargeCoursePercentage            = DefaultConfig.LargeCoursePercentage,
            AnnouncementsPerCourse           = DefaultConfig.AnnouncementsPerCourse,
            AnnouncementRetractionPercentage = 100
        };
        var events  = _sut.Generate(BuildContext(), config);

        var postedCount    = events.Count(e => e.Event.Event is CourseAnnouncementPostedEvent);
        var retractedCount = events.Count(e => e.Event.Event is CourseAnnouncementRetractedEvent);

        Assert.Equal(postedCount, retractedCount);
    }

    [Fact]
    public void Generate_WithZeroRetractionPercentage_ProducesNoRetractedEvents()
    {
        var config = new SeedingConfiguration
        {
            CourseCount                      = DefaultConfig.CourseCount,
            SmallCoursePercentage            = DefaultConfig.SmallCoursePercentage,
            MediumCoursePercentage           = DefaultConfig.MediumCoursePercentage,
            LargeCoursePercentage            = DefaultConfig.LargeCoursePercentage,
            AnnouncementsPerCourse           = DefaultConfig.AnnouncementsPerCourse,
            AnnouncementRetractionPercentage = 0
        };
        var events = _sut.Generate(BuildContext(), config);

        Assert.DoesNotContain(events, e => e.Event.Event is CourseAnnouncementRetractedEvent);
    }
}
