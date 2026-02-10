using Microsoft.Extensions.Options;

namespace Opossum.Projections;

/// <summary>
/// Validates ProjectionOptions configuration using IValidateOptions pattern.
/// This runs at startup when options are first accessed.
/// </summary>
public sealed class ProjectionOptionsValidator : IValidateOptions<ProjectionOptions>
{
    public ValidateOptionsResult Validate(string? name, ProjectionOptions options)
    {
        var failures = new List<string>();

        // Validate PollingInterval
        if (options.PollingInterval < TimeSpan.FromMilliseconds(100))
        {
            failures.Add($"PollingInterval must be at least 100ms, got {options.PollingInterval}");
        }

        if (options.PollingInterval > TimeSpan.FromHours(1))
        {
            failures.Add($"PollingInterval must be at most 1 hour, got {options.PollingInterval}");
        }

        // Validate BatchSize
        if (options.BatchSize < 1)
        {
            failures.Add($"BatchSize must be at least 1, got {options.BatchSize}");
        }

        if (options.BatchSize > 100000)
        {
            failures.Add($"BatchSize must be at most 100,000, got {options.BatchSize}");
        }

        // Validate MaxConcurrentRebuilds
        if (options.MaxConcurrentRebuilds < 1)
        {
            failures.Add($"MaxConcurrentRebuilds must be at least 1, got {options.MaxConcurrentRebuilds}");
        }

        if (options.MaxConcurrentRebuilds > 64)
        {
            failures.Add($"MaxConcurrentRebuilds must be at most 64, got {options.MaxConcurrentRebuilds}. " +
                "Extremely high values can cause performance issues.");
        }

        // Warning for potentially problematic values (not failures, just info)
        if (options.MaxConcurrentRebuilds > 16 && failures.Count == 0)
        {
            // This is not a failure, just a note. We'll allow it but could log a warning.
            // For now, we won't add it to failures.
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
