using Opossum.Projections;

namespace Opossum.UnitTests.Projections;

public class ProjectionCheckpointTests
{
    [Fact]
    public void Constructor_InitializesProperties()
    {
        // Arrange & Act
        var checkpoint = new ProjectionCheckpoint();

        // Assert
        Assert.NotNull(checkpoint.ProjectionName);
        Assert.Equal(string.Empty, checkpoint.ProjectionName);
        Assert.Equal(0, checkpoint.LastProcessedPosition);
        Assert.Equal(default, checkpoint.LastUpdated);
        Assert.Equal(0, checkpoint.TotalEventsProcessed);
    }

    [Fact]
    public void ProjectionName_CanBeSet()
    {
        // Arrange
        var checkpoint = new ProjectionCheckpoint();

        // Act
        checkpoint.ProjectionName = "TestProjection";

        // Assert
        Assert.Equal("TestProjection", checkpoint.ProjectionName);
    }

    [Fact]
    public void LastProcessedPosition_CanBeSet()
    {
        // Arrange
        var checkpoint = new ProjectionCheckpoint();

        // Act
        checkpoint.LastProcessedPosition = 12345;

        // Assert
        Assert.Equal(12345, checkpoint.LastProcessedPosition);
    }

    [Fact]
    public void LastUpdated_CanBeSet()
    {
        // Arrange
        var checkpoint = new ProjectionCheckpoint();
        var timestamp = DateTimeOffset.UtcNow;

        // Act
        checkpoint.LastUpdated = timestamp;

        // Assert
        Assert.Equal(timestamp, checkpoint.LastUpdated);
    }

    [Fact]
    public void TotalEventsProcessed_CanBeSet()
    {
        // Arrange
        var checkpoint = new ProjectionCheckpoint();

        // Act
        checkpoint.TotalEventsProcessed = 99999;

        // Assert
        Assert.Equal(99999, checkpoint.TotalEventsProcessed);
    }

    [Fact]
    public void Checkpoint_CanBeFullyPopulated()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;

        // Act
        var checkpoint = new ProjectionCheckpoint
        {
            ProjectionName = "OrderSummary",
            LastProcessedPosition = 5000,
            LastUpdated = timestamp,
            TotalEventsProcessed = 5000
        };

        // Assert
        Assert.Equal("OrderSummary", checkpoint.ProjectionName);
        Assert.Equal(5000, checkpoint.LastProcessedPosition);
        Assert.Equal(timestamp, checkpoint.LastUpdated);
        Assert.Equal(5000, checkpoint.TotalEventsProcessed);
    }
}
