namespace Opossum.Samples.DataSeeder.Core;

/// <summary>
/// Orchestrates all registered generators, produces a temporally sorted event stream,
/// assigns sequential positions, and hands the result to an <see cref="IEventWriter"/>.
/// </summary>
public sealed class SeedPlan
{
    private readonly IReadOnlyList<ISeedGenerator> _generators;

    /// <param name="generators">
    /// Generators in dependency order — each generator may read state written by earlier ones
    /// via <see cref="SeedContext"/>.
    /// </param>
    public SeedPlan(IReadOnlyList<ISeedGenerator> generators)
    {
        _generators = generators;
    }

    /// <summary>
    /// Runs all generators, sorts the combined event stream by timestamp (stable sort),
    /// assigns 1-based relative positions, then writes the result via <paramref name="writer"/>.
    /// </summary>
    /// <returns>The total number of events written.</returns>
    public async Task<int> RunAsync(
        SeedingConfiguration config,
        IEventWriter writer,
        string contextPath,
        CancellationToken cancellationToken = default)
    {
        var context = new SeedContext();
        var allEvents = new List<SeedEvent>();

        foreach (var generator in _generators)
        {
            var events = generator.Generate(context, config);
            allEvents.AddRange(events);
        }

        // LINQ OrderBy is stable — events at the same timestamp preserve their generator order.
        var sorted = allEvents.OrderBy(e => e.Metadata.Timestamp).ToList();

        // Assign 1-based relative positions. DirectEventWriter adds the existing ledger offset.
        var sequenced = sorted
            .Select((e, i) => new SequencedSeedEvent(i + 1, e.Event, e.Metadata))
            .ToList();

        await writer.WriteAsync(sequenced, contextPath, cancellationToken);
        return sequenced.Count;
    }
}
