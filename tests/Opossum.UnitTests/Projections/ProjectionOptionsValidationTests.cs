using Microsoft.Extensions.Options;
using Opossum.Projections;

namespace Opossum.UnitTests.Projections;

/// <summary>
/// Unit tests for ProjectionOptions validation.
/// Tests the validation logic to ensure invalid configurations are rejected.
/// </summary>
public sealed class ProjectionOptionsValidationTests
{
    [Fact]
    public void Validate_ValidOptions_ReturnsSuccess()
    {
        // Arrange
        var options = new ProjectionOptions
        {
            PollingInterval = TimeSpan.FromSeconds(5),
            BatchSize = 1000,
            MaxConcurrentRebuilds = 4,
            EnableAutoRebuild = true
        };

        var validator = new ProjectionOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]  // Too low
    [InlineData(50)] // Below minimum (100ms)
    public void Validate_PollingIntervalTooLow_ReturnsFail(int milliseconds)
    {
        // Arrange
        var options = new ProjectionOptions
        {
            PollingInterval = TimeSpan.FromMilliseconds(milliseconds),
            BatchSize = 1000,
            MaxConcurrentRebuilds = 4
        };

        var validator = new ProjectionOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("at least 100ms", string.Join(", ", result.Failures));
    }

    [Fact]
    public void Validate_PollingIntervalTooHigh_ReturnsFail()
    {
        // Arrange
        var options = new ProjectionOptions
        {
            PollingInterval = TimeSpan.FromHours(2),  // Above maximum (1 hour)
            BatchSize = 1000,
            MaxConcurrentRebuilds = 4
        };

        var validator = new ProjectionOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("at most 1 hour", string.Join(", ", result.Failures));
    }

    [Theory]
    [InlineData(100)]      // Minimum valid
    [InlineData(1000)]     // Common value (1 second)
    [InlineData(5000)]     // Default (5 seconds)
    [InlineData(3600000)]  // Maximum valid (1 hour)
    public void Validate_PollingIntervalValidRange_ReturnsSuccess(int milliseconds)
    {
        // Arrange
        var options = new ProjectionOptions
        {
            PollingInterval = TimeSpan.FromMilliseconds(milliseconds),
            BatchSize = 1000,
            MaxConcurrentRebuilds = 4
        };

        var validator = new ProjectionOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_BatchSizeTooLow_ReturnsFail(int batchSize)
    {
        // Arrange
        var options = new ProjectionOptions
        {
            PollingInterval = TimeSpan.FromSeconds(5),
            BatchSize = batchSize,
            MaxConcurrentRebuilds = 4
        };

        var validator = new ProjectionOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("at least 1", string.Join(", ", result.Failures));
    }

    [Fact]
    public void Validate_BatchSizeTooHigh_ReturnsFail()
    {
        // Arrange
        var options = new ProjectionOptions
        {
            PollingInterval = TimeSpan.FromSeconds(5),
            BatchSize = 100001,  // Above maximum
            MaxConcurrentRebuilds = 4
        };

        var validator = new ProjectionOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("at most 100,000", string.Join(", ", result.Failures));
    }

    [Theory]
    [InlineData(1)]       // Minimum
    [InlineData(50)]      // Common
    [InlineData(1000)]    // Default
    [InlineData(10000)]   // Large
    [InlineData(100000)]  // Maximum
    public void Validate_BatchSizeValidRange_ReturnsSuccess(int batchSize)
    {
        // Arrange
        var options = new ProjectionOptions
        {
            PollingInterval = TimeSpan.FromSeconds(5),
            BatchSize = batchSize,
            MaxConcurrentRebuilds = 4
        };

        var validator = new ProjectionOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_MaxConcurrentRebuildsTooLow_ReturnsFail(int maxConcurrent)
    {
        // Arrange
        var options = new ProjectionOptions
        {
            PollingInterval = TimeSpan.FromSeconds(5),
            BatchSize = 1000,
            MaxConcurrentRebuilds = maxConcurrent
        };

        var validator = new ProjectionOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("at least 1", string.Join(", ", result.Failures));
    }

    [Fact]
    public void Validate_MaxConcurrentRebuildsTooHigh_ReturnsFail()
    {
        // Arrange
        var options = new ProjectionOptions
        {
            PollingInterval = TimeSpan.FromSeconds(5),
            BatchSize = 1000,
            MaxConcurrentRebuilds = 65  // Above maximum
        };

        var validator = new ProjectionOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("at most 64", string.Join(", ", result.Failures));
    }

    [Theory]
    [InlineData(1)]   // Minimum
    [InlineData(2)]   // HDD recommended
    [InlineData(4)]   // Default/SSD recommended
    [InlineData(8)]   // NVMe recommended
    [InlineData(16)]  // High-end
    [InlineData(32)]  // RAID arrays
    [InlineData(64)]  // Maximum
    public void Validate_MaxConcurrentRebuildsValidRange_ReturnsSuccess(int maxConcurrent)
    {
        // Arrange
        var options = new ProjectionOptions
        {
            PollingInterval = TimeSpan.FromSeconds(5),
            BatchSize = 1000,
            MaxConcurrentRebuilds = maxConcurrent
        };

        var validator = new ProjectionOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_MultipleInvalidValues_ReturnsAllErrors()
    {
        // Arrange
        var options = new ProjectionOptions
        {
            PollingInterval = TimeSpan.FromMilliseconds(50),  // Too low
            BatchSize = 0,  // Too low
            MaxConcurrentRebuilds = 100  // Too high
        };

        var validator = new ProjectionOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Equal(3, result.Failures.Count());  // Should have 3 distinct failures
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_EnableAutoRebuild_AcceptsBothValues(bool enableAutoRebuild)
    {
        // Arrange
        var options = new ProjectionOptions
        {
            PollingInterval = TimeSpan.FromSeconds(5),
            BatchSize = 1000,
            MaxConcurrentRebuilds = 4,
            EnableAutoRebuild = enableAutoRebuild
        };

        var validator = new ProjectionOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }
}
