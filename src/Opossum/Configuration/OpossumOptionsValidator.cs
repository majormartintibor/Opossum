using Microsoft.Extensions.Options;

namespace Opossum.Configuration;

/// <summary>
/// Validates OpossumOptions configuration using IValidateOptions pattern.
/// This runs at startup when options are first accessed.
/// </summary>
public sealed class OpossumOptionsValidator : IValidateOptions<OpossumOptions>
{
    public ValidateOptionsResult Validate(string? name, OpossumOptions options)
    {
        var failures = new List<string>();

        // Validate RootPath
        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            failures.Add("RootPath cannot be null or empty");
        }
        else
        {
            // Check if path contains invalid characters
            if (options.RootPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                failures.Add($"RootPath contains invalid characters: {options.RootPath}");
            }

            // Check if path is rooted (absolute) - cross-platform aware
            // Windows: C:\path, D:\path, \\server\share
            // Linux: /path
            if (!Path.IsPathRooted(options.RootPath))
            {
                // On Linux, relative paths starting with ~ are common but not supported by Path.IsPathRooted
                // We require absolute paths for consistency
                failures.Add($"RootPath must be an absolute path: {options.RootPath}. " +
                    $"Examples: Windows 'C:\\Database', Linux '/var/opossum/data'");
            }
        }

        // Validate Contexts
        if (options.Contexts.Count == 0)
        {
            failures.Add("At least one context must be configured. Use AddContext() to add contexts.");
        }

        foreach (var context in options.Contexts)
        {
            if (string.IsNullOrWhiteSpace(context))
            {
                failures.Add("Context names cannot be null or whitespace");
                continue;
            }

            if (!IsValidDirectoryName(context))
            {
                failures.Add($"Invalid context name '{context}'. Context names must be valid directory names.");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static bool IsValidDirectoryName(string name)
    {
        try
        {
            // Check for invalid path characters
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return false;

            // Check for reserved names (Windows)
            var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4",
                "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3",
                "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

            return !reserved.Contains(name.ToUpperInvariant());
        }
        catch
        {
            return false;
        }
    }
}
