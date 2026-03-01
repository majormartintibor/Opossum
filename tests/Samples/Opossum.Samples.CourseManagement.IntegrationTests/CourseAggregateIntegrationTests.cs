using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Opossum.Samples.CourseManagement.IntegrationTests;

/// <summary>
/// Integration tests for the Event-Sourced Aggregate endpoints.
///
/// These tests exercise POST /courses/aggregate, PATCH /courses/aggregate/{id}/capacity,
/// and POST /courses/aggregate/{id}/subscriptions — the alternative write-side pattern
/// that sits alongside the DCB Decision Model endpoints in the same sample application.
///
/// Both endpoint families share the same event log. The aggregate endpoints are tested
/// independently here to keep test isolation clean.
/// </summary>
[Collection("Integration Tests")]
public class CourseAggregateIntegrationTests
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

    public CourseAggregateIntegrationTests(IntegrationTestFixture fixture)
    {
        _client = fixture.Client;
    }

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateCourse_ValidRequest_ReturnsCreatedAsync()
    {
        var response = await _client.PostAsJsonAsync("/courses/aggregate", new
        {
            Name = "Aggregate Course",
            Description = "Created via aggregate",
            MaxStudents = 20
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptions);
        Assert.True(result.TryGetProperty("id", out var idProp));
        Assert.True(Guid.TryParse(idProp.GetString(), out _));
    }

    // ── Change capacity ──────────────────────────────────────────────────────

    [Fact]
    public async Task ChangeCapacity_ValidRequest_ReturnsOkAsync()
    {
        var courseId = await CreateCourseAsync(capacity: 10);

        var response = await _client.PatchAsJsonAsync(
            $"/courses/aggregate/{courseId}/capacity",
            new { NewCapacity = 25 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ChangeCapacity_NonExistentCourse_ReturnsNotFoundAsync()
    {
        var response = await _client.PatchAsJsonAsync(
            $"/courses/aggregate/{Guid.NewGuid()}/capacity",
            new { NewCapacity = 10 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ChangeCapacity_SameCapacity_ReturnsBadRequestAsync()
    {
        var courseId = await CreateCourseAsync(capacity: 10);

        var response = await _client.PatchAsJsonAsync(
            $"/courses/aggregate/{courseId}/capacity",
            new { NewCapacity = 10 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("already has a capacity", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangeCapacity_BelowCurrentEnrollment_ReturnsBadRequestAsync()
    {
        var courseId = await CreateCourseAsync(capacity: 5);
        var student1 = await CreateStudentAsync();
        var student2 = await CreateStudentAsync();

        // Subscribe 2 registered students
        await SubscribeStudentAsync(courseId, student1);
        await SubscribeStudentAsync(courseId, student2);

        // Try to shrink capacity below current enrollment
        var response = await _client.PatchAsJsonAsync(
            $"/courses/aggregate/{courseId}/capacity",
            new { NewCapacity = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("active subscriptions", body, StringComparison.OrdinalIgnoreCase);
    }

    // ── Subscribe student ────────────────────────────────────────────────────

    [Fact]
    public async Task SubscribeStudent_ValidRequest_ReturnsCreatedAsync()
    {
        var courseId = await CreateCourseAsync(capacity: 10);
        var studentId = await CreateStudentAsync();

        var response = await SubscribeStudentAsync(courseId, studentId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task SubscribeStudent_NonExistentCourse_ReturnsNotFoundAsync()
    {
        var studentId = await CreateStudentAsync();

        var response = await SubscribeStudentAsync(Guid.NewGuid(), studentId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SubscribeStudent_UnregisteredStudent_ReturnsBadRequestAsync()
    {
        var courseId = await CreateCourseAsync(capacity: 10);

        var response = await SubscribeStudentAsync(courseId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("not registered", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubscribeStudent_CourseAtCapacity_ReturnsBadRequestAsync()
    {
        var courseId = await CreateCourseAsync(capacity: 1);
        var student1 = await CreateStudentAsync();
        var student2 = await CreateStudentAsync();

        // Fill the single available seat
        await SubscribeStudentAsync(courseId, student1);

        // Try to subscribe one more student — course is now full
        var response = await SubscribeStudentAsync(courseId, student2);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("fully booked", body, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> CreateCourseAsync(int capacity = 30)
    {
        var response = await _client.PostAsJsonAsync("/courses/aggregate", new
        {
            Name = $"Course {Guid.NewGuid():N}",
            Description = "Test course",
            MaxStudents = capacity
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptions);
        return Guid.Parse(result.GetProperty("id").GetString()!);
    }

    private async Task<Guid> CreateStudentAsync()
    {
        var response = await _client.PostAsJsonAsync("/students", new
        {
            FirstName = "Test",
            LastName = "Student",
            Email = $"aggregate.test.{Guid.NewGuid()}@example.com"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptions);
        return Guid.Parse(result.GetProperty("id").GetString()!);
    }

    private Task<HttpResponseMessage> SubscribeStudentAsync(Guid courseId, Guid studentId) =>
        _client.PostAsJsonAsync(
            $"/courses/aggregate/{courseId}/subscriptions",
            new { StudentId = studentId });
}
