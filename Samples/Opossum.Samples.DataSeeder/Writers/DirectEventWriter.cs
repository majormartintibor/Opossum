using Opossum.Core;
using Opossum.Samples.DataSeeder.Core;

namespace Opossum.Samples.DataSeeder.Writers;

/// <summary>
/// High-performance event writer that writes directly to the Opossum file system layout,
/// bypassing the event store's per-event locking and index-update path.
/// </summary>
/// <remarks>
/// <para>
/// Accumulates all index structures in memory across the entire batch, then flushes each
/// index file exactly once regardless of how many events reference it. This gives O(1)
/// per-event I/O for index updates instead of the O(n) rewrite-per-event pattern of the
/// normal <see cref="Opossum.IEventStore.AppendAsync"/> path.
/// </para>
/// <para>
/// Event files are written in parallel (configurable via <see cref="DirectEventWriter(int)"/>),
/// index files and the ledger are written sequentially after all event files are on disk.
/// Uses the same atomic temp-file + rename strategy as <c>EventFileManager</c>.
/// </para>
/// <para>
/// <b>No DCB enforcement.</b> The caller must guarantee data integrity. This writer is
/// intended for developer tooling (the DataSeeder) where invariants are enforced by the
/// generators, not at write time.
/// </para>
/// </remarks>
public sealed class DirectEventWriter : IEventWriter
{
    private static readonly char[] _invalidFileNameChars = Path.GetInvalidFileNameChars();
    private static readonly JsonSerializerOptions _metaOptions = new() { WriteIndented = true };

    private readonly int _parallelism;

    /// <param name="parallelism">
    /// Maximum number of concurrent event-file writes.
    /// Pass 0 (the default) to use <see cref="Environment.ProcessorCount"/>.
    /// </param>
    public DirectEventWriter(int parallelism = 0)
    {
        _parallelism = parallelism <= 0 ? Environment.ProcessorCount : parallelism;
    }

    /// <inheritdoc />
    public async Task WriteAsync(
        IReadOnlyList<SequencedSeedEvent> events,
        string contextPath,
        IProgress<WriterProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0) return;

        // Read existing ledger so we can offset relative positions to absolute ones.
        var startOffset = await ReadLastPositionAsync(contextPath).ConfigureAwait(false);

        // Ensure directory structure exists.
        Directory.CreateDirectory(Path.Combine(contextPath, "events"));
        Directory.CreateDirectory(Path.Combine(contextPath, "Indices", "EventType"));
        Directory.CreateDirectory(Path.Combine(contextPath, "Indices", "Tags"));

        // Build the complete in-memory index map before any I/O — O(total tags) time.
        var indexMap = BuildIndexMap(events, contextPath, startOffset);

        var serializer = new SeedEventSerializer();
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _parallelism,
            CancellationToken = cancellationToken
        };

        // ── Phase 1: Write event files in parallel ────────────────────────────
        const long ReportEvery = 5_000;
        var totalEvents = (long)events.Count;
        long eventsDone = 0;

        progress?.Report(new WriterProgress
        {
            PhaseName = "Writing event files",
            PhaseNumber = 1,
            TotalPhases = 2,
            Current = 0,
            Total = totalEvents
        });

        await Parallel.ForEachAsync(events, parallelOptions, async (seedEvent, ct) =>
        {
            var absolutePosition = seedEvent.Position + startOffset;
            var sequencedEvent = new SequencedEvent
            {
                Event = seedEvent.Event,
                Position = absolutePosition,
                Metadata = seedEvent.Metadata
            };

            var json = serializer.Serialize(sequencedEvent);
            var filePath = Path.Combine(contextPath, "events", $"{absolutePosition:D10}.json");
            await WriteAtomicAsync(filePath, json, ct).ConfigureAwait(false);

            var done = Interlocked.Increment(ref eventsDone);
            if (done % ReportEvery == 0 || done == totalEvents)
                progress?.Report(new WriterProgress
                {
                    PhaseName = "Writing event files",
                    PhaseNumber = 1,
                    TotalPhases = 2,
                    Current = done,
                    Total = totalEvents
                });
        }).ConfigureAwait(false);

        // ── Phase 2: Write index files sequentially ───────────────────────────
        var totalIndexFiles = (long)indexMap.Count;
        var indexReportEvery = Math.Max(1L, totalIndexFiles / 200);
        long indexDone = 0;

        progress?.Report(new WriterProgress
        {
            PhaseName = "Writing index files",
            PhaseNumber = 2,
            TotalPhases = 2,
            Current = 0,
            Total = totalIndexFiles
        });

        foreach (var (indexFilePath, newPositions) in indexMap)
        {
            await WriteIndexFileAsync(indexFilePath, newPositions).ConfigureAwait(false);
            indexDone++;
            if (indexDone % indexReportEvery == 0 || indexDone == totalIndexFiles)
                progress?.Report(new WriterProgress
                {
                    PhaseName = "Writing index files",
                    PhaseNumber = 2,
                    TotalPhases = 2,
                    Current = indexDone,
                    Total = totalIndexFiles
                });
        }

        // Write ledger last — it is the commit record.
        var lastAbsolutePosition = events.Max(e => e.Position) + startOffset;
        await WriteLedgerAsync(contextPath, lastAbsolutePosition, startOffset + events.Count)
            .ConfigureAwait(false);
    }

    // ── Index map ────────────────────────────────────────────────────────────

    private static Dictionary<string, SortedSet<long>> BuildIndexMap(
        IReadOnlyList<SequencedSeedEvent> events,
        string contextPath,
        long offset)
    {
        var map = new Dictionary<string, SortedSet<long>>(StringComparer.OrdinalIgnoreCase);
        var eventTypePath = Path.Combine(contextPath, "Indices", "EventType");
        var tagsPath = Path.Combine(contextPath, "Indices", "Tags");

        foreach (var e in events)
        {
            var absolutePosition = e.Position + offset;

            // EventType index
            var etFile = Path.Combine(eventTypePath, $"{SanitizeFileName(e.Event.EventType)}.json");
            AddToMap(map, etFile, absolutePosition);

            // Tag indices
            foreach (var tag in e.Event.Tags)
            {
                var tagFile = Path.Combine(
                    tagsPath,
                    $"{SanitizeFileName(tag.Key)}_{SanitizeFileName(tag.Value)}.json");
                AddToMap(map, tagFile, absolutePosition);
            }
        }

        return map;
    }

    private static void AddToMap(
        Dictionary<string, SortedSet<long>> map,
        string path,
        long position)
    {
        if (!map.TryGetValue(path, out var set))
        {
            set = [];
            map[path] = set;
        }
        set.Add(position);
    }

    // ── File writers ─────────────────────────────────────────────────────────

    private static async Task WriteIndexFileAsync(
        string indexFilePath,
        SortedSet<long> newPositions)
    {
        // Merge new positions with any that already exist in the file.
        var merged = new SortedSet<long>(newPositions);
        if (File.Exists(indexFilePath))
        {
            try
            {
                var existingJson = await File.ReadAllTextAsync(indexFilePath).ConfigureAwait(false);
                var existing = JsonSerializer.Deserialize<IndexData>(existingJson, _metaOptions);
                foreach (var p in existing?.Positions ?? [])
                    merged.Add(p);
            }
            catch { /* corrupt file — overwrite with new positions only */ }
        }

        var json = JsonSerializer.Serialize(
            new IndexData { Positions = [.. merged] },
            _metaOptions);
        await WriteAtomicAsync(indexFilePath, json).ConfigureAwait(false);
    }

    private static async Task WriteLedgerAsync(
        string contextPath,
        long lastPosition,
        long eventCount)
    {
        var ledgerPath = Path.Combine(contextPath, ".ledger");
        var json = JsonSerializer.Serialize(
            new LedgerData { LastSequencePosition = lastPosition, EventCount = eventCount },
            _metaOptions);
        await WriteAtomicAsync(ledgerPath, json).ConfigureAwait(false);
    }

    private static async Task<long> ReadLastPositionAsync(string contextPath)
    {
        var ledgerPath = Path.Combine(contextPath, ".ledger");
        if (!File.Exists(ledgerPath)) return 0;

        try
        {
            var json = await File.ReadAllTextAsync(ledgerPath).ConfigureAwait(false);
            var data = JsonSerializer.Deserialize<LedgerData>(json, _metaOptions);
            return data?.LastSequencePosition ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static async Task WriteAtomicAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken = default)
    {
        var tempPath = filePath + $".tmp.{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(tempPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
                try { File.Delete(tempPath); } catch { /* ignore cleanup errors */ }
            throw;
        }
    }

    private static string SanitizeFileName(string input) =>
        string.Join("_", input.Split(_invalidFileNameChars, StringSplitOptions.RemoveEmptyEntries));

    // ── Private DTOs (match the shapes Opossum writes) ───────────────────────

    private sealed record LedgerData
    {
        public long LastSequencePosition { get; init; }
        public long EventCount { get; init; }
    }

    private sealed record IndexData
    {
        public List<long> Positions { get; init; } = [];
    }
}
