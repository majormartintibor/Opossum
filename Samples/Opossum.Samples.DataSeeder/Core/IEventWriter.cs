namespace Opossum.Samples.DataSeeder.Core;

/// <summary>
/// Writes a batch of pre-sequenced seed events to an event store context directory.
/// </summary>
public interface IEventWriter
{
    /// <summary>
    /// Persists all <paramref name="events"/> to the store at <paramref name="contextPath"/>.
    /// </summary>
    /// <param name="events">
    /// Events with pre-assigned relative positions. Writers interpret positions as offsets from
    /// the current end of the store (i.e., position 1 means the next event after the last one).
    /// </param>
    /// <param name="contextPath">
    /// Absolute path to the event store context directory
    /// (e.g. <c>D:\Database\OpossumSampleApp</c>).
    /// <see cref="Writers.EventStoreWriter"/> ignores this parameter.
    /// </param>
    Task WriteAsync(
        IReadOnlyList<SequencedSeedEvent> events,
        string contextPath,
        CancellationToken cancellationToken = default);
}
