using System.ComponentModel.DataAnnotations;

namespace Opossum.Configuration;

/// <summary>
/// Configuration options for the Opossum event store
/// </summary>
public sealed class OpossumOptions
{
    /// <summary>
    /// Root directory path for storing events and indices.
    /// Must be a valid, accessible directory path.
    /// Default: "OpossumStore"
    /// </summary>
    [Required(ErrorMessage = "RootPath is required")]
    [MinLength(1, ErrorMessage = "RootPath cannot be empty")]
    public string RootPath { get; set; } = "OpossumStore";

    /// <summary>
    /// List of configured bounded contexts.
    /// Each context gets its own isolated event store directory.
    /// 
    /// ⚠️ MVP LIMITATION: Only the FIRST context is currently used.
    /// While you can add multiple contexts, only Contexts[0] will be used for storage.
    /// See docs/limitations/mvp-single-context.md for details.
    /// 
    /// Configure exactly ONE context in MVP:
    /// <code>
    /// options.AddContext("CourseManagement"); // ✅ This will be used
    /// // Don't add more contexts - they will be ignored in MVP
    /// </code>
    /// </summary>
    public List<string> Contexts { get; } = [];

    /// <summary>
    /// When true, forces events to be physically written to disk (flushed) before append completes.
    /// This guarantees durability at the cost of performance (~1-5ms per event on modern SSDs).
    /// 
    /// Why this matters:
    /// - TRUE (default): Events are guaranteed on disk before AppendAsync returns. 
    ///   Safe for production. Prevents data loss on power failure.
    /// - FALSE: Events may only be in OS page cache. Faster but risky. 
    ///   Only use for testing or when you accept potential data loss.
    /// 
    /// Default: true (recommended for production)
    /// Performance impact: ~1-5ms per event on SSD
    /// </summary>
    public bool FlushEventsImmediately { get; set; } = true;

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
    /// Validates that a string is a valid directory name.
    /// Uses a consistent set of forbidden characters across all platforms
    /// to ensure predictable behavior and prevent confusing directory names.
    /// </summary>
    private static bool IsValidDirectoryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        // Explicitly forbidden characters (consistent across platforms)
        // These are problematic for directory names regardless of OS:
        // / \ : * ? " < > | (path separators, wildcards, reserved on Windows)
        // \0 (null character, invalid everywhere)
        char[] forbiddenChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0' };

        // Check if name contains any forbidden characters
        return !name.Any(c => forbiddenChars.Contains(c));
    }
}
