using Opossum.Projections;

namespace Opossum.UnitTests.Projections;

public class ProjectionMetadataTests
{
    [Fact]
    public void ProjectionMetadata_CanBeCreated()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;

        // Act
        var metadata = new ProjectionMetadata
        {
            CreatedAt = now,
            LastUpdatedAt = now,
            Version = 1,
            SizeInBytes = 256
        };

        // Assert
        Assert.Equal(now, metadata.CreatedAt);
        Assert.Equal(now, metadata.LastUpdatedAt);
        Assert.Equal(1, metadata.Version);
        Assert.Equal(256, metadata.SizeInBytes);
    }

    [Fact]
    public void ProjectionMetadata_SupportsWithSyntax()
    {
        // Arrange
        var original = new ProjectionMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-7),
            LastUpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            Version = 5,
            SizeInBytes = 512
        };

        // Act
        var updated = original with
        {
            LastUpdatedAt = DateTimeOffset.UtcNow,
            Version = 6,
            SizeInBytes = 600
        };

        // Assert
        Assert.Equal(original.CreatedAt, updated.CreatedAt); // CreatedAt unchanged
        Assert.NotEqual(original.LastUpdatedAt, updated.LastUpdatedAt);
        Assert.Equal(6, updated.Version);
        Assert.Equal(600, updated.SizeInBytes);
    }
}
