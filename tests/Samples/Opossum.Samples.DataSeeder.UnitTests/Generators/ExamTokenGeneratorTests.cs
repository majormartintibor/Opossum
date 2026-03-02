using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.DataSeeder;
using Opossum.Samples.DataSeeder.Core;
using Opossum.Samples.DataSeeder.Generators;

namespace Opossum.Samples.DataSeeder.UnitTests.Generators;

/// <summary>
/// Unit tests for <see cref="ExamTokenGenerator"/>.
/// Verifies token lifecycle ordering, mutual exclusion of redeemed/revoked,
/// and that redemption students are enrolled in the corresponding course.
/// </summary>
public sealed class ExamTokenGeneratorTests
{
    private static SeedingConfiguration DefaultConfig => new()
    {
        StudentCount               = 20,
        BasicTierPercentage        = 25,
        StandardTierPercentage     = 50,
        ProfessionalTierPercentage = 25,
        MasterTierPercentage       = 0,
        CourseCount                = 4,
        SmallCoursePercentage      = 100,
        MediumCoursePercentage     = 0,
        LargeCoursePercentage      = 0,
        ExamsPerCourse             = 2,
        TokensPerExam              = 5,
        TokenRedemptionPercentage  = 70,
        TokenRevocationPercentage  = 10
    };

    private static SeedContext BuildContext()
    {
        var context = new SeedContext(randomSeed: 42);
        new StudentGenerator().Generate(context, DefaultConfig);
        new CourseGenerator().Generate(context, DefaultConfig);
        new EnrollmentGenerator().Generate(context, DefaultConfig);
        return context;
    }

    private readonly ExamTokenGenerator _sut = new();

    [Fact]
    public void Generate_ProducesExactlyOneIssuedEventPerToken()
    {
        var context   = BuildContext();
        var events    = _sut.Generate(context, DefaultConfig);
        var expectedTokenCount = DefaultConfig.CourseCount
                                 * DefaultConfig.ExamsPerCourse
                                 * DefaultConfig.TokensPerExam;

        var issuedCount = events.Count(e => e.Event.Event is ExamRegistrationTokenIssuedEvent);

        Assert.Equal(expectedTokenCount, issuedCount);
    }

    [Fact]
    public void Generate_IssuedEventPayloadsAreCorrectType()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var issued = events.Where(e => e.Event.Event is ExamRegistrationTokenIssuedEvent);

        Assert.All(issued, e => Assert.IsType<ExamRegistrationTokenIssuedEvent>(e.Event.Event));
    }

    [Fact]
    public void Generate_TokenIdsAreUnique()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var tokenIds = events
            .Select(e => e.Event.Event switch
            {
                ExamRegistrationTokenIssuedEvent   i => i.TokenId,
                ExamRegistrationTokenRedeemedEvent r => r.TokenId,
                ExamRegistrationTokenRevokedEvent  v => v.TokenId,
                _                                  => Guid.Empty
            })
            .Where(id => id != Guid.Empty)
            .GroupBy(id => id)
            .Select(g => (TokenId: g.Key, Count: g.Count()))
            .ToList();

        // Each tokenId should appear at most twice: once for issued + once for redeemed/revoked.
        Assert.All(tokenIds, t => Assert.True(t.Count <= 2,
            $"TokenId {t.TokenId} appeared {t.Count} times — expected at most 2."));
    }

    [Fact]
    public void Generate_RedemptionAndRevocationAreMutuallyExclusivePerToken()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var redeemedTokenIds = events
            .Where(e => e.Event.Event is ExamRegistrationTokenRedeemedEvent)
            .Select(e => ((ExamRegistrationTokenRedeemedEvent)e.Event.Event).TokenId)
            .ToHashSet();

        var revokedTokenIds = events
            .Where(e => e.Event.Event is ExamRegistrationTokenRevokedEvent)
            .Select(e => ((ExamRegistrationTokenRevokedEvent)e.Event.Event).TokenId)
            .ToHashSet();

        Assert.Empty(redeemedTokenIds.Intersect(revokedTokenIds));
    }

    [Fact]
    public void Generate_IssuedTimestampPrecedesRedeemedTimestamp()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var issuedAt = events
            .Where(e => e.Event.Event is ExamRegistrationTokenIssuedEvent)
            .ToDictionary(
                e => ((ExamRegistrationTokenIssuedEvent)e.Event.Event).TokenId,
                e => e.Metadata.Timestamp);

        var redeemedEvents = events.Where(e => e.Event.Event is ExamRegistrationTokenRedeemedEvent);

        Assert.All(redeemedEvents, e =>
        {
            var redeemed = (ExamRegistrationTokenRedeemedEvent)e.Event.Event;
            Assert.True(issuedAt.TryGetValue(redeemed.TokenId, out var issued));
            Assert.True(e.Metadata.Timestamp > issued,
                $"Redeemed timestamp {e.Metadata.Timestamp} is not after issued timestamp {issued}.");
        });
    }

    [Fact]
    public void Generate_IssuedTimestampPrecedesRevokedTimestamp()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var issuedAt = events
            .Where(e => e.Event.Event is ExamRegistrationTokenIssuedEvent)
            .ToDictionary(
                e => ((ExamRegistrationTokenIssuedEvent)e.Event.Event).TokenId,
                e => e.Metadata.Timestamp);

        var revokedEvents = events.Where(e => e.Event.Event is ExamRegistrationTokenRevokedEvent);

        Assert.All(revokedEvents, e =>
        {
            var revoked = (ExamRegistrationTokenRevokedEvent)e.Event.Event;
            Assert.True(issuedAt.TryGetValue(revoked.TokenId, out var issued));
            Assert.True(e.Metadata.Timestamp > issued,
                $"Revoked timestamp {e.Metadata.Timestamp} is not after issued timestamp {issued}.");
        });
    }

    [Fact]
    public void Generate_RedeemedStudentIsEnrolledInTokenCourse()
    {
        var context = BuildContext();
        var events  = _sut.Generate(context, DefaultConfig);

        // Build tokenId → courseId map from issued events.
        var tokenCourse = events
            .Where(e => e.Event.Event is ExamRegistrationTokenIssuedEvent)
            .ToDictionary(
                e => ((ExamRegistrationTokenIssuedEvent)e.Event.Event).TokenId,
                e => ((ExamRegistrationTokenIssuedEvent)e.Event.Event).CourseId);

        var redeemedEvents = events.Where(e => e.Event.Event is ExamRegistrationTokenRedeemedEvent);

        Assert.All(redeemedEvents, e =>
        {
            var redeemed = (ExamRegistrationTokenRedeemedEvent)e.Event.Event;
            Assert.True(tokenCourse.TryGetValue(redeemed.TokenId, out var courseId));
            Assert.Contains((redeemed.StudentId, courseId), context.EnrolledPairs);
        });
    }

    [Fact]
    public void Generate_EachIssuedEventHasExamTokenTag()
    {
        var events  = _sut.Generate(BuildContext(), DefaultConfig);
        var issued  = events.Where(e => e.Event.Event is ExamRegistrationTokenIssuedEvent);

        Assert.All(issued, e => Assert.Contains(e.Event.Tags, t => t.Key == "examToken"));
    }

    [Fact]
    public void Generate_EachIssuedEventHasExamIdTag()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);
        var issued = events.Where(e => e.Event.Event is ExamRegistrationTokenIssuedEvent);

        Assert.All(issued, e => Assert.Contains(e.Event.Tags, t => t.Key == "examId"));
    }

    [Fact]
    public void Generate_EachIssuedEventHasCourseIdTag()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);
        var issued = events.Where(e => e.Event.Event is ExamRegistrationTokenIssuedEvent);

        Assert.All(issued, e => Assert.Contains(e.Event.Tags, t => t.Key == "courseId"));
    }

    [Fact]
    public void Generate_EachRedeemedEventHasStudentIdTag()
    {
        var events   = _sut.Generate(BuildContext(), DefaultConfig);
        var redeemed = events.Where(e => e.Event.Event is ExamRegistrationTokenRedeemedEvent);

        Assert.All(redeemed, e => Assert.Contains(e.Event.Tags, t => t.Key == "studentId"));
    }

    [Fact]
    public void Generate_ReturnsEmptyWhenNoCoursesExist()
    {
        var events = _sut.Generate(new SeedContext(randomSeed: 42), DefaultConfig);

        Assert.Empty(events);
    }

    [Fact]
    public void Generate_ReturnsEmptyWhenExamsPerCourseIsZero()
    {
        var config = new SeedingConfiguration
        {
            StudentCount               = DefaultConfig.StudentCount,
            BasicTierPercentage        = DefaultConfig.BasicTierPercentage,
            StandardTierPercentage     = DefaultConfig.StandardTierPercentage,
            ProfessionalTierPercentage = DefaultConfig.ProfessionalTierPercentage,
            MasterTierPercentage       = DefaultConfig.MasterTierPercentage,
            CourseCount                = DefaultConfig.CourseCount,
            SmallCoursePercentage      = DefaultConfig.SmallCoursePercentage,
            MediumCoursePercentage     = DefaultConfig.MediumCoursePercentage,
            LargeCoursePercentage      = DefaultConfig.LargeCoursePercentage,
            ExamsPerCourse             = 0,
            TokensPerExam              = DefaultConfig.TokensPerExam,
            TokenRedemptionPercentage  = DefaultConfig.TokenRedemptionPercentage,
            TokenRevocationPercentage  = DefaultConfig.TokenRevocationPercentage
        };
        var events = _sut.Generate(BuildContext(), config);

        Assert.Empty(events);
    }

    [Fact]
    public void Generate_WithNoEnrolledStudents_ProducesNoRedeemedEvents()
    {
        // Context with courses but no enrollments — redemption requires enrolled students.
        var context = new SeedContext(randomSeed: 42);
        new CourseGenerator().Generate(context, DefaultConfig);

        var events = _sut.Generate(context, DefaultConfig);

        Assert.DoesNotContain(events, e => e.Event.Event is ExamRegistrationTokenRedeemedEvent);
    }

    [Fact]
    public void Generate_ExamIdTagMatchesPayloadExamId()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);
        var issued = events.Where(e => e.Event.Event is ExamRegistrationTokenIssuedEvent);

        Assert.All(issued, e =>
        {
            var issuedEvent = (ExamRegistrationTokenIssuedEvent)e.Event.Event;
            var examIdTag   = e.Event.Tags.First(t => t.Key == "examId").Value;
            Assert.Equal(issuedEvent.ExamId.ToString(), examIdTag);
        });
    }

    [Fact]
    public void Generate_TokenTagMatchesPayloadTokenId()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);
        var issued = events.Where(e => e.Event.Event is ExamRegistrationTokenIssuedEvent);

        Assert.All(issued, e =>
        {
            var issuedEvent = (ExamRegistrationTokenIssuedEvent)e.Event.Event;
            var tokenTag    = e.Event.Tags.First(t => t.Key == "examToken").Value;
            Assert.Equal(issuedEvent.TokenId.ToString(), tokenTag);
        });
    }
}
