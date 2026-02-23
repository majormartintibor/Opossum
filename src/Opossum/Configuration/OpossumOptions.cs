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
    /// The name of this event store. Used as a subdirectory under <see cref="RootPath"/>.
    /// Set exactly once via <see cref="UseStore"/>.
    /// </summary>
    public string? StoreName { get; private set; }

    /// <summary>
    /// When true, forces events to be physically written to disk (flushed) before append completes.
    /// This guarantees durability at the cost of performance.
    /// 
    /// Benchmarked Performance (SSD, Windows 11, .NET 10):
    /// - TRUE (flush enabled): ~10ms per event, ~100 events/sec throughput
    /// - FALSE (no flush): ~4.5ms per event, ~220 events/sec throughput
    /// 
    /// Why this matters:
    /// - TRUE (default): Events are guaranteed on disk before AppendAsync returns. 
    ///   Safe for production. Prevents data loss on power failure.
    /// - FALSE: Events may only be in OS page cache. Faster but risky. 
    ///   Only use for testing or when you accept potential data loss.
    /// 
    /// Note: FlushEventsImmediately = false provides ~2.2x speedup but risks data loss.
    /// See docs/benchmarking/results/20260212/ANALYSIS.md for detailed benchmarks.
    /// 
    /// Default: true (recommended for production)
    /// </summary>
    public bool FlushEventsImmediately { get; set; } = true;

    /// <summary>
    /// When true, committed event files are marked read-only at the OS level immediately
    /// after being written to disk. This prevents accidental modification or deletion of
    /// immutable event records.
    ///
    /// What this provides:
    /// - Committed event files cannot be opened for writing by any process without
    ///   explicitly removing the read-only attribute first.
    /// - On Windows, File Explorer warns before deleting a read-only file.
    /// - Satisfies the common compliance requirement that audit records "cannot be altered"
    ///   (ISO 9001:2015 clause 7.5.3.2, EU GMP Annex 11 section 4.8, HACCP documentation).
    ///
    /// What this does NOT provide:
    /// - Protection against a Windows Administrator or root user who explicitly removes
    ///   the read-only attribute and then modifies or deletes the file.
    /// - On Linux, deletion is controlled by the parent directory's write permission,
    ///   not the file's own permission, so read-only does not prevent deletion by the
    ///   directory owner.
    ///
    /// The additive maintenance operation (<see cref="IEventStoreMaintenance.AddTagsAsync"/>)
    /// automatically unprotects a file before rewriting it and re-applies protection
    /// afterward. Write protection is transparent to all Opossum operations.
    ///
    /// Default: false. Enable in production environments where event files must not be
    /// accidentally modified or deleted by operators browsing the store directory.
    /// </summary>
    public bool WriteProtectEventFiles { get; set; } = false;

    /// <summary>
    /// When true, projection files are marked read-only at the OS level immediately
    /// after being written to disk. This prevents accidental modification or deletion
    /// by humans or other processes while Opossum transparently manages unprotecting
    /// files before updates or rebuilds and re-applying protection afterwards.
    ///
    /// What this provides:
    /// - Projection files cannot be opened for writing by any process without
    ///   explicitly removing the read-only attribute first.
    /// - On Windows, File Explorer warns before deleting a read-only file.
    ///
    /// What this does NOT provide:
    /// - Protection against a Windows Administrator or root user who explicitly removes
    ///   the read-only attribute and then modifies or deletes the file.
    /// - On Linux, deletion is controlled by the parent directory's write permission,
    ///   not the file's own permission, so read-only does not prevent deletion by the
    ///   directory owner.
    ///
    /// Unlike event files, projection files are derived state and can always be rebuilt
    /// from events. Write protection here is purely about preventing accidental edits
    /// by humans browsing the store directory.
    ///
    /// Default: false. Enable in production environments where projection files must not
    /// be accidentally modified or deleted by operators browsing the store directory.
    /// </summary>
    public bool WriteProtectProjectionFiles { get; set; } = false;

    /// <summary>
    /// Sets the name of this event store.
    /// <see cref="RootPath"/> and must be a valid directory name.
    /// </summary>
    /// <param name="name">
    /// The store name. Must be a valid directory name (no path separators or wildcards).
    /// </param>
    /// <returns>This options instance for fluent configuration.</returns>
    /// <exception cref="ArgumentException">Thrown when the name is null, empty, or contains invalid characters.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="UseStore"/> has already been called. Opossum supports exactly
    /// one store per instance. To use multiple isolated event stores, register separate
    /// <see cref="IEventStore"/> instances with different <see cref="RootPath"/> values.
    /// </exception>
    public OpossumOptions UseStore(string name)
    {
        if (StoreName is not null)
        {
            throw new InvalidOperationException(
                $"UseStore has already been called with '{StoreName}'. " +
                "Opossum supports exactly one store per instance. " +
                "To use multiple isolated event stores, register separate IEventStore instances " +
                "with different RootPath values.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Store name cannot be empty", nameof(name));
        }

        if (!IsValidDirectoryName(name))
        {
            throw new ArgumentException(
                $"Invalid store name: '{name}'. Store names must be valid directory names.",
                nameof(name));
        }

        StoreName = name;
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
        char[] forbiddenChars = ['/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0'];

        // Check if name contains any forbidden characters
        return !name.Any(c => forbiddenChars.Contains(c));
    }
}
