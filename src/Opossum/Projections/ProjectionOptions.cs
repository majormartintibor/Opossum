using System.ComponentModel.DataAnnotations;

namespace Opossum.Projections;

/// <summary>
/// Configuration options for the projection system
/// </summary>
public sealed class ProjectionOptions
{
    /// <summary>
    /// How often the projection daemon polls for new events
    /// Default: 5 seconds
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "01:00:00",
        ErrorMessage = "PollingInterval must be between 100ms and 1 hour")]
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum number of events to process in a single batch
    /// Default: 1000
    /// </summary>
    [Range(1, 100000, ErrorMessage = "BatchSize must be between 1 and 100,000")]
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Maximum number of events to load per batch during a projection rebuild.
    ///
    /// Lower values reduce peak memory usage but increase the number of index reads
    /// (each batch re-reads the event-type position index).
    /// Higher values reduce I/O round-trips but use more memory.
    ///
    /// GUIDANCE:
    /// - Constrained memory (&lt;512 MB heap): 1 000 – 5 000
    /// - Normal workloads:                   5 000 (default)
    /// - High-memory / fast NVMe:            10 000 – 50 000
    ///
    /// Default: 5 000
    /// </summary>
    [Range(100, 1000000, ErrorMessage = "RebuildBatchSize must be between 100 and 1,000,000")]
    public int RebuildBatchSize { get; set; } = 5_000;

    /// <summary>
    /// Number of events processed between rebuild journal flushes.
    ///
    /// Controls the maximum re-work required on crash recovery.
    /// After every <c>RebuildFlushInterval</c> events the rebuilder persists
    /// a journal checkpoint and the current tag accumulator to disk.
    /// If the process crashes, at most this many events need to be re-processed.
    ///
    /// GUIDANCE:
    /// - Lower values = more durable, but more journal write overhead
    /// - Higher values = less journal overhead, but more re-work on recovery
    /// - Very low values (&lt;1 000) may noticeably slow down rebuilds due to frequent I/O
    ///
    /// Default: 10 000
    /// </summary>
    [Range(100, 1_000_000, ErrorMessage = "RebuildFlushInterval must be between 100 and 1,000,000")]
    public int RebuildFlushInterval { get; set; } = 10_000;

    /// <summary>
    /// Controls how the projection daemon handles projections at startup.
    /// Default: <see cref="AutoRebuildMode.MissingCheckpointsOnly"/>
    /// </summary>
    public AutoRebuildMode AutoRebuild { get; set; } = AutoRebuildMode.MissingCheckpointsOnly;

    /// <summary>
    /// Maximum number of projections to rebuild concurrently.
    ///
    /// DISK TYPE RECOMMENDATIONS:
    /// - HDD (single disk): 2-4
    /// - SSD: 4-8
    /// - NVMe SSD: 8-16
    /// - RAID array: 16-32
    ///
    /// IMPORTANT: Higher values improve rebuild speed but increase:
    /// - CPU usage (may slow HTTP requests)
    /// - Memory usage (all events loaded in parallel)
    /// - Disk I/O contention
    ///
    /// Default: 4 (balanced for most scenarios)
    /// </summary>
    [Range(1, 64, ErrorMessage = "MaxConcurrentRebuilds must be between 1 and 64")]
    public int MaxConcurrentRebuilds { get; set; } = 4;

    /// <summary>
    /// Assemblies to scan for projection definitions
    /// </summary>
    public List<Assembly> ScanAssemblies { get; } = [];

    /// <summary>
    /// Discovered tag providers mapped by projection name.
    /// Populated during assembly scanning.
    /// </summary>
    internal Dictionary<string, Type> TagProviders { get; } = [];

    /// <summary>
    /// Adds an assembly to scan for projection definitions
    /// </summary>
    /// <param name="assembly">Assembly to scan</param>
    /// <returns>This options instance for fluent configuration</returns>
    public ProjectionOptions ScanAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (!ScanAssemblies.Contains(assembly))
        {
            ScanAssemblies.Add(assembly);
        }

        return this;
    }
}
