using System.Reflection;
using Opossum.Projections;

namespace Opossum.UnitTests.Projections;

public class ProjectionOptionsTests
{
    [Fact]
    public void Constructor_SetsDefaultValues()
    {
        // Arrange & Act
        var options = new ProjectionOptions();

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(5), options.PollingInterval);
        Assert.Equal(1000, options.BatchSize);
        Assert.True(options.EnableAutoRebuild);
        Assert.NotNull(options.ScanAssemblies);
        Assert.Empty(options.ScanAssemblies);
    }

    [Fact]
    public void PollingInterval_CanBeSet()
    {
        // Arrange
        var options = new ProjectionOptions();
        var interval = TimeSpan.FromSeconds(10);

        // Act
        options.PollingInterval = interval;

        // Assert
        Assert.Equal(interval, options.PollingInterval);
    }

    [Fact]
    public void BatchSize_CanBeSet()
    {
        // Arrange
        var options = new ProjectionOptions();

        // Act
        options.BatchSize = 500;

        // Assert
        Assert.Equal(500, options.BatchSize);
    }

    [Fact]
    public void EnableAutoRebuild_CanBeSet()
    {
        // Arrange
        var options = new ProjectionOptions();

        // Act
        options.EnableAutoRebuild = false;

        // Assert
        Assert.False(options.EnableAutoRebuild);
    }

    [Fact]
    public void ScanAssembly_WithValidAssembly_AddsAssembly()
    {
        // Arrange
        var options = new ProjectionOptions();
        var assembly = typeof(ProjectionOptions).Assembly;

        // Act
        var result = options.ScanAssembly(assembly);

        // Assert
        Assert.Single(options.ScanAssemblies);
        Assert.Contains(assembly, options.ScanAssemblies);
        Assert.Same(options, result); // Fluent API
    }

    [Fact]
    public void ScanAssembly_WithMultipleAssemblies_AddsAll()
    {
        // Arrange
        var options = new ProjectionOptions();
        var assembly1 = typeof(ProjectionOptions).Assembly;
        var assembly2 = typeof(ProjectionOptionsTests).Assembly;

        // Act
        options.ScanAssembly(assembly1)
               .ScanAssembly(assembly2);

        // Assert
        Assert.Equal(2, options.ScanAssemblies.Count);
        Assert.Contains(assembly1, options.ScanAssemblies);
        Assert.Contains(assembly2, options.ScanAssemblies);
    }

    [Fact]
    public void ScanAssembly_WithDuplicateAssembly_DoesNotAddTwice()
    {
        // Arrange
        var options = new ProjectionOptions();
        var assembly = typeof(ProjectionOptions).Assembly;

        // Act
        options.ScanAssembly(assembly)
               .ScanAssembly(assembly);

        // Assert
        Assert.Single(options.ScanAssemblies);
    }

    [Fact]
    public void ScanAssembly_WithNullAssembly_ThrowsArgumentNullException()
    {
        // Arrange
        var options = new ProjectionOptions();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => options.ScanAssembly(null!));
    }
}
