namespace Opossum.Projections;

/// <summary>
/// Plain data model representing the state of an in-progress projection rebuild.
/// Serialised as JSON to <c>{projectionName}.rebuild.json</c> in the checkpoint directory
/// every <c>RebuildFlushInterval</c> events so that a crashed rebuild can be resumed from
/// <see cref="ResumeFromPosition"/> instead of restarting from position 0.
/// <para>
/// This class contains no file I/O logic — it is a pure data model consumed by
/// <c>ProjectionRebuilder</c> for JSON serialisation and deserialisation.
/// </para>
/// </summary>
internal sealed class ProjectionRebuildJournal
{
    /// <summary>
    /// Name of the projection being rebuilt.
    /// </summary>
    public required string ProjectionName { get; set; }

    /// <summary>
    /// Absolute path to the temporary directory where rebuild output is written.
    /// On resume, the rebuilder continues writing into this same directory so that
    /// previously written projection files are preserved.
    /// </summary>
    public required string TempPath { get; set; }

    /// <summary>
    /// The store head position captured at the start of the rebuild.
    /// After the rebuild completes, the checkpoint is advanced to at least this position.
    /// </summary>
    public long StoreHeadAtStart { get; set; }

    /// <summary>
    /// The last event position that was safely flushed to the temp directory.
    /// On resume, event replay starts from the first position after this value.
    /// Updated atomically each time the journal is flushed.
    /// </summary>
    public long ResumeFromPosition { get; set; }

    /// <summary>
    /// UTC timestamp when this rebuild was started.
    /// </summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// UTC timestamp of the most recent journal flush.
    /// Updated atomically each time the journal is flushed.
    /// </summary>
    public DateTimeOffset LastFlushedAt { get; set; }
}
