using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Opossum.Samples.CourseManagement.IntegrationTests;

/// <summary>
/// Integration tests for the Course Announcement endpoints.
///
/// Covers the DCB "Prevent Record Duplication" pattern:
/// https://dcb.events/examples/prevent-record-duplication/
///
/// Key scenarios verified:
/// - First post with a fresh token succeeds.
/// - Re-submission with the same token is detected and rejected.
/// - Two different tokens for the same course never interfere.
/// - After retraction, the original token is freed and the post can be re-submitted.
/// - Retracting a non-existent or already-retracted announcement is rejected.
/// </summary>
[Collection("Integration Tests")]
public class CourseAnnouncementIntegrationTests : IClassFixture<IntegrationTestFixture>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CourseAnnouncementIntegrationTests(IntegrationTestFixture fixture)
    {
        _client = fixture.Client;
    }

    // -------------------------------------------------------------------------
    // Post announcement
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostAnnouncement_ValidRequest_ReturnsCreatedAsync()
    {
        var courseId = await CreateCourseAsync();
        var token = Guid.NewGuid();

        var response = await PostAnnouncementAsync(courseId, token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(Guid.TryParse(body.GetProperty("announcementId").GetString(), out _));
    }

    [Fact]
    public async Task PostAnnouncement_SameTokenTwice_SecondCallReturnsBadRequestAsync()
    {
        var courseId = await CreateCourseAsync();
        var token = Guid.NewGuid();

        var first = await PostAnnouncementAsync(courseId, token);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await PostAnnouncementAsync(courseId, token);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var content = await second.Content.ReadAsStringAsync();
        Assert.Contains("Re-submission detected", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostAnnouncement_DifferentTokens_BothSucceedAsync()
    {
        var courseId = await CreateCourseAsync();

        var first = await PostAnnouncementAsync(courseId, Guid.NewGuid());
        var second = await PostAnnouncementAsync(courseId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task PostAnnouncement_NonExistentCourse_ReturnsBadRequestAsync()
    {
        var response = await PostAnnouncementAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Course does not exist", content, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Retract announcement
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RetractAnnouncement_ExistingAnnouncement_ReturnsOkAsync()
    {
        var courseId = await CreateCourseAsync();
        var token = Guid.NewGuid();
        await PostAnnouncementAsync(courseId, token);

        var response = await RetractAnnouncementAsync(courseId, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RetractAnnouncement_NonExistentToken_ReturnsBadRequestAsync()
    {
        var courseId = await CreateCourseAsync();

        var response = await RetractAnnouncementAsync(courseId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Announcement not found", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetractAnnouncement_AlreadyRetracted_ReturnsBadRequestAsync()
    {
        var courseId = await CreateCourseAsync();
        var token = Guid.NewGuid();
        await PostAnnouncementAsync(courseId, token);
        await RetractAnnouncementAsync(courseId, token);

        var second = await RetractAnnouncementAsync(courseId, token);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var content = await second.Content.ReadAsStringAsync();
        Assert.Contains("already been retracted", content, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Token reuse after retraction
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostAnnouncement_SameTokenAfterRetraction_SucceedsAsync()
    {
        // The DCB spec note: "allow a token to be reused once the order was placed."
        // After retraction the IdempotencyTokenWasUsed projection folds:
        //   Posted(T) → true, then Retracted(T) → false.
        // Final state is false — the token is free.
        var courseId = await CreateCourseAsync();
        var token = Guid.NewGuid();

        var firstPost = await PostAnnouncementAsync(courseId, token);
        Assert.Equal(HttpStatusCode.Created, firstPost.StatusCode);

        var retract = await RetractAnnouncementAsync(courseId, token);
        Assert.Equal(HttpStatusCode.OK, retract.StatusCode);

        var repost = await PostAnnouncementAsync(courseId, token, title: "Corrected title");

        Assert.Equal(HttpStatusCode.Created, repost.StatusCode);
    }

    [Fact]
    public async Task PostAnnouncement_NewTokenAfterRetraction_AlsoSucceedsAsync()
    {
        var courseId = await CreateCourseAsync();
        var originalToken = Guid.NewGuid();

        await PostAnnouncementAsync(courseId, originalToken);
        await RetractAnnouncementAsync(courseId, originalToken);

        var response = await PostAnnouncementAsync(courseId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Guid> CreateCourseAsync()
    {
        var response = await _client.PostAsJsonAsync("/courses", new
        {
            Name = $"Announcement Test Course {Guid.NewGuid():N}",
            Description = "Test",
            MaxStudentCount = 30
        });
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        return Guid.Parse(body.GetProperty("id").GetString()!);
    }

    private Task<HttpResponseMessage> PostAnnouncementAsync(
        Guid courseId,
        Guid token,
        string title = "Test Announcement",
        string body = "This is an important announcement.") =>
        _client.PostAsJsonAsync($"/courses/{courseId}/announcements", new
        {
            title,
            body,
            idempotencyToken = token
        });

    private Task<HttpResponseMessage> RetractAnnouncementAsync(Guid courseId, Guid token) =>
        _client.PostAsync($"/courses/{courseId}/announcements/{token}/retract", content: null);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json, _jsonOptions);
    }
}
