namespace Opossum.Configuration;

/// <summary>
/// Configuration options for the Opossum event store
/// </summary>
public sealed class OpossumOptions
{
    /// <summary>
    /// Root directory path for storing events and indices.
    /// Default: "OpossumStore"
    /// </summary>
    public string RootPath { get; set; } = "OpossumStore";

    /// <summary>
    /// List of configured bounded contexts.
    /// Each context gets its own isolated event store directory.
    /// </summary>
    public List<string> Contexts { get; } = new();

    /// <summary>
    /// Adds a bounded context to the event store.
    /// </summary>
    /// <param name="contextName">Name of the context (must be valid directory name)</param>
    /// <returns>This options instance for fluent configuration</returns>
    /// <exception cref="ArgumentException">Thrown when context name is invalid</exception>
    /// <exception cref="InvalidOperationException">Thrown when context already exists</exception>
    public OpossumOptions AddContext(string contextName)
    {
        if (string.IsNullOrWhiteSpace(contextName))
        {
            throw new ArgumentException("Context name cannot be empty", nameof(contextName));
        }

        if (!IsValidDirectoryName(contextName))
        {
            throw new ArgumentException(
                $"Invalid context name: '{contextName}'. Context names must be valid directory names.",
                nameof(contextName));
        }

        if (Contexts.Contains(contextName, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Context '{contextName}' has already been added");
        }

        Contexts.Add(contextName);
        return this;
    }

    /// <summary>
    /// Validates that a string is a valid directory name
    /// </summary>
    private static bool IsValidDirectoryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        // Get invalid file name characters (covers directory names too)
        char[] invalidChars = Path.GetInvalidFileNameChars();

        // Check if name contains any invalid characters
        return !name.Any(c => invalidChars.Contains(c));
    }
}
