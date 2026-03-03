using Opossum;
using Opossum.Core;
using Opossum.Samples.DataSeeder.Core;

namespace Opossum.Samples.DataSeeder.Writers;

/// <summary>
/// Fallback <see cref="IEventWriter"/> that delegates to <see cref="IEventStore.AppendAsync"/>.
/// Suitable for small datasets (&lt; ~50 K events) where correctness guarantees from the event
/// store are preferred over maximum write throughput.
/// </summary>
/// <remarks>
/// <para>
/// Positions in <see cref="SequencedSeedEvent"/> are ignored — the event store assigns its own
/// positions. The <c>contextPath</c> argument is also ignored.
/// </para>
/// <para>
/// Events are sent in chunks of <see cref="ChunkSize"/> per <c>AppendAsync</c> call, which
/// reduces cross-process lock acquisitions compared to appending one event at a time while
/// still staying within a reasonable batch size.
/// </para>
/// </remarks>
public sealed class EventStoreWriter : IEventWriter
{
    private readonly IEventStore _eventStore;

    /// <summary>Number of events per <c>AppendAsync</c> call.</summary>
    public int ChunkSize { get; }

    /// <param name="eventStore">The event store to append events to.</param>
    /// <param name="chunkSize">Events per <c>AppendAsync</c> call. Default: 100.</param>
    public EventStoreWriter(IEventStore eventStore, int chunkSize = 100)
    {
        _eventStore = eventStore;
        ChunkSize = chunkSize;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <paramref name="contextPath"/> is ignored. The <see cref="IEventStore"/> manages its own
    /// storage path. <see cref="SequencedSeedEvent.Position"/> values are also ignored.
    /// </remarks>
    public async Task WriteAsync(
        IReadOnlyList<SequencedSeedEvent> events,
        string contextPath,
        IProgress<WriterProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var written = 0;
        progress?.Report(new WriterProgress
        {
            PhaseName = "Appending events",
            PhaseNumber = 1,
            TotalPhases = 1,
            Current = 0,
            Total = events.Count
        });

        for (int i = 0; i < events.Count; i += ChunkSize)
        {
            var chunk = events
                .Skip(i)
                .Take(ChunkSize)
                .Select(e => new NewEvent { Event = e.Event, Metadata = e.Metadata })
                .ToArray();

            await _eventStore.AppendAsync(chunk, null, cancellationToken).ConfigureAwait(false);
            written += chunk.Length;
            progress?.Report(new WriterProgress
            {
                PhaseName = "Appending events",
                PhaseNumber = 1,
                TotalPhases = 1,
                Current = written,
                Total = events.Count
            });
        }
    }
}
