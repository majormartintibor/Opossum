using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Opossum.Samples.CourseManagement.IntegrationTests;

[Collection("Integration Tests")]
public class StudentRegistrationIntegrationTests
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

    public StudentRegistrationIntegrationTests(IntegrationTestFixture fixture)
    {
        _client = fixture.Client;
    }

    [Fact]
    public async Task RegisterStudent_ValidRequest_ReturnsCreated()
    {
        // Arrange
        var request = new
        {
            FirstName = "John",
            LastName = "Doe",
            Email = $"john.doe.{Guid.NewGuid()}@example.com"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/students", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("id", out var idProperty));
        Assert.True(Guid.TryParse(idProperty.GetString(), out _));
    }

    [Fact]
    public async Task RegisterStudent_DuplicateEmail_ReturnsBadRequest()
    {
        // Arrange
        var email = $"duplicate.{Guid.NewGuid()}@example.com";
        var request1 = new { FirstName = "John", LastName = "Doe", Email = email };
        var request2 = new { FirstName = "Jane", LastName = "Smith", Email = email };

        // Act
        var response1 = await _client.PostAsJsonAsync("/students", request1);
        var response2 = await _client.PostAsJsonAsync("/students", request2);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, response2.StatusCode);

        var errorContent = await response2.Content.ReadAsStringAsync();
        Assert.Contains("email already exists", errorContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegisterStudent_ConcurrentDuplicateEmail_OnlyOneSucceeds()
    {
        // Arrange
        var email = $"concurrent.{Guid.NewGuid()}@example.com";
        var request1 = new { FirstName = "John", LastName = "Doe", Email = email };
        var request2 = new { FirstName = "Jane", LastName = "Smith", Email = email };

        // Act - Both try to register with the same email concurrently
        // Both will check for email uniqueness, both see it's available, both try to register
        // DCB's FailIfEventsMatch ensures exactly one succeeds and one fails
        var task1 = _client.PostAsJsonAsync("/students", request1);
        var task2 = _client.PostAsJsonAsync("/students", request2);

        var responses = await Task.WhenAll(task1, task2);

        // Assert - EXACTLY one should succeed, EXACTLY one should fail
        // This validates DCB's email uniqueness enforcement under concurrent access
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(1, successCount);
        Assert.Equal(1, conflictCount);

        // Verify the conflict response contains Problem Details
        var conflictResponse = responses.First(r => r.StatusCode == HttpStatusCode.Conflict);
        var problemDetails = await conflictResponse.Content.ReadAsStringAsync();
        Assert.Contains("Conflict", problemDetails, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegisterStudent_Then_GetStudent_ReturnsCorrectData()
    {
        // Arrange
        var email = $"test.{Guid.NewGuid()}@example.com";
        var registerRequest = new
        {
            FirstName = "Alice",
            LastName = "Johnson",
            Email = email
        };

        // Act - Register
        var registerResponse = await _client.PostAsJsonAsync("/students", registerRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var registerContent = await registerResponse.Content.ReadAsStringAsync();
        var registerResult = JsonSerializer.Deserialize<JsonElement>(registerContent, _jsonOptions);
        var studentId = registerResult.GetProperty("id").GetString();

        // Wait for projections to update
        await Task.Delay(TimeSpan.FromSeconds(6));

        // Act - Get student
        var getResponse = await _client.GetAsync($"/students/{studentId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var getContent = await getResponse.Content.ReadAsStringAsync();
        var student = JsonSerializer.Deserialize<JsonElement>(getContent, _jsonOptions);

        Assert.Equal("Alice", student.GetProperty("firstName").GetString());
        Assert.Equal("Johnson", student.GetProperty("lastName").GetString());
        Assert.Equal(email, student.GetProperty("email").GetString());
        Assert.Equal("Basic", student.GetProperty("enrollmentTier").GetString());
    }
}
