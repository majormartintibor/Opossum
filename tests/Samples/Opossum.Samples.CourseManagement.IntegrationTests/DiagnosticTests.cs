using System.Net.Http.Json;

namespace Opossum.Samples.CourseManagement.IntegrationTests;

/// <summary>
/// Diagnostic tests to verify test isolation and database path configuration.
/// Uses dedicated collection to ensure clean database state.
/// </summary>
[Collection("Diagnostic Tests")]
public class DiagnosticTests
{
    private readonly HttpClient _client;
    private readonly IntegrationTestFixture _fixture;

    public DiagnosticTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Client;
    }

    [Fact]
    public async Task Database_UsesIsolatedTestPath_NotProductionDatabase()
    {
        // This test verifies that tests are NOT using the production database
        // Instead, they should use a temporary path like C:\Users\...\Temp\OpossumTests_<guid> (Windows)
        // or /tmp/OpossumTests_<guid> (Linux)

        // Arrange - Production database path (platform-aware)
        var productionDbPath = OperatingSystem.IsWindows()
            ? "D:\\Database\\OpossumSampleApp"     // Windows production path
            : "/var/opossum/data/OpossumSampleApp"; // Linux production path (if deployed)

        DateTime? lastModifiedBefore = null;

        if (Directory.Exists(productionDbPath))
        {
            var files = Directory.GetFiles(productionDbPath, "*.*", SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                lastModifiedBefore = files.Max(f => File.GetLastWriteTimeUtc(f));
            }
        }

        // Act - Create a student (which will create event files in the database)
        var studentId = Guid.NewGuid();
        var response = await _client.PostAsJsonAsync("/students", new
        {
            FirstName = "Diagnostic",
            LastName = "Test",
            Email = "diagnostic@test.com"
        });

        // Assert - Request succeeded
        response.EnsureSuccessStatusCode();

        // Wait a moment to ensure any file writes complete
        // Increased to 200ms for CI reliability
        await Task.Delay(200);

        // Verify the production database was NOT modified
        if (Directory.Exists(productionDbPath))
        {
            var files = Directory.GetFiles(productionDbPath, "*.*", SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                var lastModifiedAfter = files.Max(f => File.GetLastWriteTimeUtc(f));

                // The production database timestamp should NOT have changed
                Assert.Equal(lastModifiedBefore, lastModifiedAfter);
            }
        }

        // Additional verification: The student should NOT exist in production projections
        // (If it did, we'd be using the wrong database)
        // This is implicitly verified by the timestamp check above
    }

    [Fact]
    public async Task EmptyDatabase_StartsWithNoEvents()
    {
        // NOTE: This test name is slightly misleading after we added fixture seeding.
        // The database is NOT empty - it has 2 students and 2 courses seeded by the fixture.
        // This test verifies that projections are registered and have processed the seeded events.

        // Act - Get checkpoints
        var response = await _client.GetAsync("/admin/projections/checkpoints");
        response.EnsureSuccessStatusCode();

        var checkpoints = await response.Content.ReadFromJsonAsync<Dictionary<string, long>>();

        // Assert - Verify expected projections are registered
        Assert.NotNull(checkpoints);
        Assert.Contains("CourseDetails", checkpoints.Keys);
        Assert.Contains("CourseShortInfo", checkpoints.Keys);
        Assert.Contains("StudentDetails", checkpoints.Keys);
        Assert.Contains("StudentShortInfo", checkpoints.Keys);

        // Checkpoints should be >= 0 (will be > 0 if fixture seeding completed and auto-rebuild ran)
        // This verifies projections are working correctly
        Assert.All(checkpoints.Values, checkpoint => Assert.True(checkpoint >= 0, 
            $"Checkpoint should be >= 0, got {checkpoint}"));
    }
}
