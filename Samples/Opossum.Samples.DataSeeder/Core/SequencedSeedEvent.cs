using Opossum.Core;

namespace Opossum.Samples.DataSeeder.Core;

/// <summary>
/// A seed event with a pre-assigned sequence position, ready for writing to the event store.
/// This is the Layer 2 → Layer 3 boundary type produced by <see cref="SeedPlan"/>.
/// </summary>
/// <remarks>
/// <see cref="Position"/> is the relative 1-based index within this batch.
/// <see cref="DirectEventWriter"/> offsets it by the existing ledger position to compute
/// the absolute position in the store.
/// </remarks>
public record SequencedSeedEvent(long Position, DomainEvent Event, Metadata Metadata);
