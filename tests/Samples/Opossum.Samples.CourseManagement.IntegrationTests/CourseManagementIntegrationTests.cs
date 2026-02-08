using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Opossum.Samples.CourseManagement.IntegrationTests;

[Collection("Integration Tests")]
public class CourseManagementIntegrationTests
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

    public CourseManagementIntegrationTests(IntegrationTestFixture fixture)
    {
        _client = fixture.Client;
    }

    [Fact]
    public async Task CreateCourse_ValidRequest_ReturnsCreated()
    {
        // Arrange
        var request = new
        {
            Name = "Introduction to Computer Science",
            Description = "Learn the basics of programming",
            MaxStudentCount = 30
        };

        // Act
        var response = await _client.PostAsJsonAsync("/courses", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("id", out var idProperty));
        Assert.True(Guid.TryParse(idProperty.GetString(), out _));
    }

    [Fact]
    public async Task ModifyCourseStudentLimit_ValidRequest_ReturnsOk()
    {
        // Arrange - Create course first
        var createRequest = new
        {
            Name = "Data Structures",
            Description = "Advanced algorithms",
            MaxStudentCount = 25
        };

        var createResponse = await _client.PostAsJsonAsync("/courses", createRequest);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        var createResult = JsonSerializer.Deserialize<JsonElement>(createContent, _jsonOptions);
        var courseId = createResult.GetProperty("id").GetString();

        // Act - Modify limit
        var modifyRequest = new { NewMaxStudentCount = 40 };
        var modifyResponse = await _client.PatchAsJsonAsync($"/courses/{courseId}/student-limit", modifyRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, modifyResponse.StatusCode);
    }

    [Fact]
    public async Task ModifyCourseStudentLimit_NonExistentCourse_ReturnsBadRequest()
    {
        // Arrange
        var nonExistentCourseId = Guid.NewGuid();
        var request = new { NewMaxStudentCount = 50 };

        // Act
        var response = await _client.PatchAsJsonAsync($"/courses/{nonExistentCourseId}/student-limit", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("does not exist", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ModifyCourseStudentLimit_InvalidLimit_ReturnsBadRequest()
    {
        // Arrange - Create course first
        var createRequest = new
        {
            Name = "Physics 101",
            Description = "Intro to Physics",
            MaxStudentCount = 20
        };

        var createResponse = await _client.PostAsJsonAsync("/courses", createRequest);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        var createResult = JsonSerializer.Deserialize<JsonElement>(createContent, _jsonOptions);
        var courseId = createResult.GetProperty("id").GetString();

        // Act - Try to set invalid limit
        var modifyRequest = new { NewMaxStudentCount = 0 };
        var modifyResponse = await _client.PatchAsJsonAsync($"/courses/{courseId}/student-limit", modifyRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, modifyResponse.StatusCode);

        var content = await modifyResponse.Content.ReadAsStringAsync();
        Assert.Contains("must be greater than zero", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCourses_AfterCreation_ReturnsCreatedCourse()
    {
        // Arrange - Create a course
        var createRequest = new
        {
            Name = $"Course_{Guid.NewGuid()}",
            Description = "Test course description",
            MaxStudentCount = 15
        };

        var createResponse = await _client.PostAsJsonAsync("/courses", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createContent = await createResponse.Content.ReadAsStringAsync();
        var createResult = JsonSerializer.Deserialize<JsonElement>(createContent, _jsonOptions);
        var courseId = createResult.GetProperty("id").GetString();

        // Wait for projections to update
        await Task.Delay(TimeSpan.FromSeconds(6));

        // Act - Get the specific course details
        var getResponse = await _client.GetAsync($"/courses/{courseId}/details");

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var content = await getResponse.Content.ReadAsStringAsync();
        var course = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);

        // Verify course details match what we created
        Assert.Equal(courseId, course.GetProperty("courseId").GetString());
        Assert.Equal(createRequest.Name, course.GetProperty("name").GetString());
        Assert.Equal(createRequest.MaxStudentCount, course.GetProperty("maxStudentCount").GetInt32());
        Assert.Equal(0, course.GetProperty("currentEnrollmentCount").GetInt32());
        Assert.False(course.GetProperty("isFull").GetBoolean());
    }
}
