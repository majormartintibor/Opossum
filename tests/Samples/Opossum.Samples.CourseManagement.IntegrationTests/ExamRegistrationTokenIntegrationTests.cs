using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Opossum.Samples.CourseManagement.IntegrationTests;

/// <summary>
/// Integration tests for the Exam Registration Token endpoints.
///
/// Covers the DCB "Opt-In Token" pattern:
/// https://dcb.events/examples/opt-in-token/
///
/// Key scenarios verified:
/// - Issue token → 201 with tokenId.
/// - Redeem valid token → 200 OK.
/// - Redeem unknown token → 400 not found.
/// - Redeem already-used token → 400 already used.
/// - Revoke token → 200 OK.
/// - Redeem revoked token → 400 revoked.
/// - Revoke already-redeemed token → 400 error.
/// </summary>
[Collection("Integration Tests")]
public class ExamRegistrationTokenIntegrationTests : IClassFixture<IntegrationTestFixture>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ExamRegistrationTokenIntegrationTests(IntegrationTestFixture fixture)
    {
        _client = fixture.Client;
    }

    // -------------------------------------------------------------------------
    // Issue token
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IssueToken_ValidRequest_ReturnsCreatedWithTokenIdAsync()
    {
        var courseId = await CreateCourseAsync();
        var examId = Guid.NewGuid();

        var response = await IssueTokenAsync(examId, courseId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(Guid.TryParse(body.GetProperty("tokenId").GetString(), out _));
    }

    [Fact]
    public async Task IssueToken_NonExistentCourse_ReturnsBadRequestAsync()
    {
        var response = await IssueTokenAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Course does not exist", content, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Redeem token
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RedeemToken_ValidToken_ReturnsOkAsync()
    {
        var tokenId = await IssueTokenAndGetIdAsync();

        var response = await RedeemTokenAsync(tokenId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RedeemToken_UnknownToken_ReturnsBadRequestAsync()
    {
        var response = await RedeemTokenAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("token not found", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RedeemToken_AlreadyRedeemedToken_ReturnsBadRequestAsync()
    {
        var tokenId = await IssueTokenAndGetIdAsync();

        var first = await RedeemTokenAsync(tokenId, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await RedeemTokenAsync(tokenId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var content = await second.Content.ReadAsStringAsync();
        Assert.Contains("already been used", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RedeemToken_RevokedToken_ReturnsBadRequestAsync()
    {
        var tokenId = await IssueTokenAndGetIdAsync();
        await RevokeTokenAsync(tokenId);

        var response = await RedeemTokenAsync(tokenId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("been revoked", content, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Revoke token
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RevokeToken_IssuedToken_ReturnsOkAsync()
    {
        var tokenId = await IssueTokenAndGetIdAsync();

        var response = await RevokeTokenAsync(tokenId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RevokeToken_UnknownToken_ReturnsBadRequestAsync()
    {
        var response = await RevokeTokenAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("token not found", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RevokeToken_AlreadyRevokedToken_ReturnsBadRequestAsync()
    {
        var tokenId = await IssueTokenAndGetIdAsync();
        await RevokeTokenAsync(tokenId);

        var second = await RevokeTokenAsync(tokenId);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var content = await second.Content.ReadAsStringAsync();
        Assert.Contains("already been revoked", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RevokeToken_AlreadyRedeemedToken_ReturnsBadRequestAsync()
    {
        var tokenId = await IssueTokenAndGetIdAsync();
        await RedeemTokenAsync(tokenId, Guid.NewGuid());

        var response = await RevokeTokenAsync(tokenId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("already been redeemed", content, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Guid> CreateCourseAsync()
    {
        var response = await _client.PostAsJsonAsync("/courses", new
        {
            Name = $"Exam Token Test Course {Guid.NewGuid():N}",
            Description = "Token test",
            MaxStudentCount = 100
        });
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        return Guid.Parse(body.GetProperty("id").GetString()!);
    }

    private async Task<Guid> IssueTokenAndGetIdAsync()
    {
        var courseId = await CreateCourseAsync();
        var examId = Guid.NewGuid();
        var response = await IssueTokenAsync(examId, courseId);
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        return Guid.Parse(body.GetProperty("tokenId").GetString()!);
    }

    private Task<HttpResponseMessage> IssueTokenAsync(Guid examId, Guid courseId) =>
        _client.PostAsJsonAsync($"/exams/{examId}/registration-tokens", new { courseId });

    private Task<HttpResponseMessage> RedeemTokenAsync(Guid tokenId, Guid studentId) =>
        _client.PostAsJsonAsync($"/exams/registration-tokens/{tokenId}/redeem", new { studentId });

    private Task<HttpResponseMessage> RevokeTokenAsync(Guid tokenId) =>
        _client.DeleteAsync($"/exams/registration-tokens/{tokenId}");

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
    }
}
