using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Opossum.Samples.CourseManagement.IntegrationTests;

[Collection("Integration Tests")]
public class StudentSubscriptionIntegrationTests
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

    public StudentSubscriptionIntegrationTests(IntegrationTestFixture fixture)
    {
        _client = fixture.Client;
    }

    [Theory]
    [InlineData("Standard")]
    [InlineData("Professional")]
    [InlineData("Master")]
    public async Task UpdateStudentSubscription_ValidTier_ReturnsOk(string tier)
    {
        // Arrange - Register student first
        var registerRequest = new
        {
            FirstName = "Test",
            LastName = "User",
            Email = $"test.{Guid.NewGuid()}@example.com"
        };

        var registerResponse = await _client.PostAsJsonAsync("/students", registerRequest);
        var registerContent = await registerResponse.Content.ReadAsStringAsync();
        var registerResult = JsonSerializer.Deserialize<JsonElement>(registerContent, _jsonOptions);
        var studentId = registerResult.GetProperty("id").GetString();

        // Act - Update subscription
        var updateRequest = new { EnrollmentTier = tier };
        var updateResponse = await _client.PatchAsJsonAsync($"/students/{studentId}/subscription", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
    }

    [Fact]
    public async Task UpdateStudentSubscription_NonExistentStudent_ReturnsBadRequest()
    {
        // Arrange
        var nonExistentStudentId = Guid.NewGuid();
        var request = new { EnrollmentTier = "Professional" };

        // Act
        var response = await _client.PatchAsJsonAsync($"/students/{nonExistentStudentId}/subscription", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("does not exist", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateStudentSubscription_Then_GetStudent_ReflectsNewTier()
    {
        // Arrange - Register student
        var registerRequest = new
        {
            FirstName = "Jane",
            LastName = "Doe",
            Email = $"jane.{Guid.NewGuid()}@example.com"
        };

        var registerResponse = await _client.PostAsJsonAsync("/students", registerRequest);
        var registerContent = await registerResponse.Content.ReadAsStringAsync();
        var registerResult = JsonSerializer.Deserialize<JsonElement>(registerContent, _jsonOptions);
        var studentId = registerResult.GetProperty("id").GetString();

        // Act - Update to Professional tier
        var updateRequest = new { EnrollmentTier = "Professional" };
        var updateResponse = await _client.PatchAsJsonAsync($"/students/{studentId}/subscription", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        // Wait for projections to update
        await Task.Delay(TimeSpan.FromSeconds(6));

        // Get student details
        var getResponse = await _client.GetAsync($"/students/{studentId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var content = await getResponse.Content.ReadAsStringAsync();
        var student = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);

        Assert.Equal("Professional", student.GetProperty("enrollmentTier").GetString());
        Assert.Equal(10, student.GetProperty("maxEnrollmentCount").GetInt32()); // Professional tier allows 10
    }
}
