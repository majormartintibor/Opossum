using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Opossum.Samples.CourseManagement.IntegrationTests;

[Collection("Integration Tests")]
public class CourseEnrollmentIntegrationTests
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

    public CourseEnrollmentIntegrationTests(IntegrationTestFixture fixture)
    {
        _client = fixture.Client;
    }

    [Fact]
    public async Task EnrollStudent_ValidRequest_ReturnsCreatedAsync()
    {
        // Arrange - Create student and course
        var (studentId, courseId) = await CreateStudentAndCourseAsync();

        // Act - Enroll student
        var enrollRequest = new { StudentId = studentId };
        var enrollResponse = await _client.PostAsJsonAsync($"/courses/{courseId}/enrollments", enrollRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Created, enrollResponse.StatusCode);
    }

    [Fact]
    public async Task EnrollStudent_DuplicateEnrollment_ReturnsBadRequestAsync()
    {
        // Arrange - Create and enroll student once
        var (studentId, courseId) = await CreateStudentAndCourseAsync();
        var enrollRequest = new { StudentId = studentId };

        var firstEnrollment = await _client.PostAsJsonAsync($"/courses/{courseId}/enrollments", enrollRequest);
        Assert.Equal(HttpStatusCode.Created, firstEnrollment.StatusCode);

        // Act - Try to enroll again
        var secondEnrollment = await _client.PostAsJsonAsync($"/courses/{courseId}/enrollments", enrollRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, secondEnrollment.StatusCode);

        var content = await secondEnrollment.Content.ReadAsStringAsync();
        Assert.Contains("already enrolled", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnrollStudent_NonExistentStudent_ReturnsBadRequestAsync()
    {
        // Arrange - Create only course
        var courseId = await CreateCourseAsync("Test Course", maxStudents: 30);
        var nonExistentStudentId = Guid.NewGuid();

        // Act
        var enrollRequest = new { StudentId = nonExistentStudentId };
        var response = await _client.PostAsJsonAsync($"/courses/{courseId}/enrollments", enrollRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("not registered", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnrollStudent_NonExistentCourse_ReturnsBadRequestAsync()
    {
        // Arrange - Create only student
        var studentId = await CreateStudentAsync($"test.{Guid.NewGuid()}@example.com");
        var nonExistentCourseId = Guid.NewGuid();

        // Act
        var enrollRequest = new { StudentId = studentId };
        var response = await _client.PostAsJsonAsync($"/courses/{nonExistentCourseId}/enrollments", enrollRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("does not exist", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnrollStudent_CourseAtCapacity_ReturnsBadRequestAsync()
    {
        // Arrange - Create course with capacity of 2
        var courseId = await CreateCourseAsync("Small Course", maxStudents: 2);

        // Enroll 2 students
        var student1 = await CreateStudentAsync($"student1.{Guid.NewGuid()}@example.com");
        var student2 = await CreateStudentAsync($"student2.{Guid.NewGuid()}@example.com");
        var student3 = await CreateStudentAsync($"student3.{Guid.NewGuid()}@example.com");

        await _client.PostAsJsonAsync($"/courses/{courseId}/enrollments", new { StudentId = student1 });
        await _client.PostAsJsonAsync($"/courses/{courseId}/enrollments", new { StudentId = student2 });

        // Act - Try to enroll third student
        var response = await _client.PostAsJsonAsync($"/courses/{courseId}/enrollments", new { StudentId = student3 });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("maximum capacity", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnrollStudent_ExceedsStudentEnrollmentLimit_ReturnsBadRequestAsync()
    {
        // Arrange - Create student with Basic tier (max 2 courses)
        var studentId = await CreateStudentAsync($"student.{Guid.NewGuid()}@example.com");

        // Create and enroll in 2 courses
        var course1 = await CreateCourseAsync("Course 1", maxStudents: 30);
        var course2 = await CreateCourseAsync("Course 2", maxStudents: 30);
        var course3 = await CreateCourseAsync("Course 3", maxStudents: 30);

        await _client.PostAsJsonAsync($"/courses/{course1}/enrollments", new { StudentId = studentId });
        await _client.PostAsJsonAsync($"/courses/{course2}/enrollments", new { StudentId = studentId });

        // Act - Try to enroll in 3rd course (exceeds Basic tier limit)
        var response = await _client.PostAsJsonAsync($"/courses/{course3}/enrollments", new { StudentId = studentId });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("enrollment limit", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnrollStudent_UpgradeTier_AllowsMoreEnrollmentsAsync()
    {
        // Arrange - Create student with Basic tier (max 2 courses)
        var studentId = await CreateStudentAsync($"student.{Guid.NewGuid()}@example.com");

        // Enroll in 2 courses
        var course1 = await CreateCourseAsync("Course 1", maxStudents: 30);
        var course2 = await CreateCourseAsync("Course 2", maxStudents: 30);

        await _client.PostAsJsonAsync($"/courses/{course1}/enrollments", new { StudentId = studentId });
        await _client.PostAsJsonAsync($"/courses/{course2}/enrollments", new { StudentId = studentId });

        // Upgrade to Standard tier (max 5 courses)
        await _client.PatchAsJsonAsync($"/students/{studentId}/subscription", new { EnrollmentTier = "Standard" });

        // Act - Enroll in 3rd, 4th, and 5th courses
        var course3 = await CreateCourseAsync("Course 3", maxStudents: 30);
        var course4 = await CreateCourseAsync("Course 4", maxStudents: 30);
        var course5 = await CreateCourseAsync("Course 5", maxStudents: 30);

        var response3 = await _client.PostAsJsonAsync($"/courses/{course3}/enrollments", new { StudentId = studentId });
        var response4 = await _client.PostAsJsonAsync($"/courses/{course4}/enrollments", new { StudentId = studentId });
        var response5 = await _client.PostAsJsonAsync($"/courses/{course5}/enrollments", new { StudentId = studentId });

        // Assert
        Assert.Equal(HttpStatusCode.Created, response3.StatusCode);
        Assert.Equal(HttpStatusCode.Created, response4.StatusCode);
        Assert.Equal(HttpStatusCode.Created, response5.StatusCode);
    }

    [Fact]
    public async Task EnrollStudent_ConcurrentEnrollments_HandlesRaceConditionAsync()
    {
        // Arrange - Create course with capacity of 1
        var courseId = await CreateCourseAsync("Tiny Course", maxStudents: 1);
        var student1 = await CreateStudentAsync($"student1.{Guid.NewGuid()}@example.com");
        var student2 = await CreateStudentAsync($"student2.{Guid.NewGuid()}@example.com");

        // Act - Try to enroll both students concurrently
        // Both will read the same state (0/1 enrolled), both will try to append
        // DCB's AfterSequencePosition ensures exactly one succeeds and one fails
        var enrollTask1 = _client.PostAsJsonAsync($"/courses/{courseId}/enrollments", new { StudentId = student1 });
        var enrollTask2 = _client.PostAsJsonAsync($"/courses/{courseId}/enrollments", new { StudentId = student2 });

        var responses = await Task.WhenAll(enrollTask1, enrollTask2);

        // Assert - EXACTLY one should succeed, EXACTLY one should fail
        // This validates DCB's optimistic concurrency control
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var failureCount = responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest);

        Assert.Equal(1, successCount);
        Assert.Equal(1, failureCount);
    }

    // Helper methods
    private async Task<(Guid studentId, Guid courseId)> CreateStudentAndCourseAsync()
    {
        var studentId = await CreateStudentAsync($"test.{Guid.NewGuid()}@example.com");
        var courseId = await CreateCourseAsync("Test Course", maxStudents: 30);
        return (studentId, courseId);
    }

    private async Task<Guid> CreateStudentAsync(string email)
    {
        var request = new
        {
            FirstName = "Test",
            LastName = "Student",
            Email = email
        };

        var response = await _client.PostAsJsonAsync("/students", request);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        return Guid.Parse(result.GetProperty("id").GetString()!);
    }

    private async Task<Guid> CreateCourseAsync(string name, int maxStudents)
    {
        var request = new
        {
            Name = name,
            Description = "Test course",
            MaxStudentCount = maxStudents
        };

        var response = await _client.PostAsJsonAsync("/courses", request);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        return Guid.Parse(result.GetProperty("id").GetString()!);
    }
}
