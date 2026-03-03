namespace Opossum.Samples.DataSeeder.Core;

/// <summary>
/// A progress snapshot emitted by an <see cref="IEventWriter"/> during a write operation.
/// Consumed by the caller to render a live progress bar.
/// </summary>
public sealed record WriterProgress
{
    /// <summary>Human-readable phase label, e.g. "Writing event files".</summary>
    public string PhaseName { get; init; } = "";

    /// <summary>1-based index of the current phase.</summary>
    public int PhaseNumber { get; init; }

    /// <summary>Total number of phases in this write operation.</summary>
    public int TotalPhases { get; init; }

    /// <summary>Number of items completed in the current phase.</summary>
    public long Current { get; init; }

    /// <summary>Total items expected in the current phase.</summary>
    public long Total { get; init; }
}
