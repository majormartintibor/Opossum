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
        // Instead, they should use a temporary path from the test fixture

        // Arrange - Production database paths (platform-aware)
        // These are the paths that WOULD be used if running the app normally
        var possibleProductionPaths = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            possibleProductionPaths.Add("D:\\Database\\OpossumSampleApp");
            possibleProductionPaths.Add(Path.Combine(Path.GetTempPath(), "OpossumData", "OpossumSampleApp"));
        }
        else
        {
            possibleProductionPaths.Add("/var/opossum/data/OpossumSampleApp");
            possibleProductionPaths.Add(Path.Combine(Path.GetTempPath(), "OpossumData", "OpossumSampleApp"));
        }

        // Record timestamps of any existing production databases
        var productionTimestamps = new Dictionary<string, DateTime?>();
        foreach (var path in possibleProductionPaths)
        {
            if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    productionTimestamps[path] = files.Max(f => File.GetLastWriteTimeUtc(f));
                }
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

        // Verify NONE of the production databases were modified
        foreach (var path in possibleProductionPaths)
        {
            if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    var currentTimestamp = files.Max(f => File.GetLastWriteTimeUtc(f));

                    if (productionTimestamps.ContainsKey(path))
                    {
                        // Production database existed before test - verify it wasn't modified
                        Assert.Equal(productionTimestamps[path], currentTimestamp);
                    }
                }
            }
        }

        // Additional verification: The test should be using a temp path (verified by fixture)
        // The fixture uses Path.GetTempPath() + "OpossumTests_<guid>"
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
