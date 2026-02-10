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
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum number of events to process in a single batch
    /// Default: 1000
    /// </summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Enable automatic projection rebuilding on startup if checkpoint is missing
    /// Default: true
    /// </summary>
    public bool EnableAutoRebuild { get; set; } = true;

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
    public int MaxConcurrentRebuilds { get; set; } = 4;

    /// <summary>
    /// Assemblies to scan for projection definitions
    /// </summary>
    public List<Assembly> ScanAssemblies { get; } = new();

    /// <summary>
    /// Discovered tag providers mapped by projection name.
    /// Populated during assembly scanning.
    /// </summary>
    internal Dictionary<string, Type> TagProviders { get; } = new();

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
