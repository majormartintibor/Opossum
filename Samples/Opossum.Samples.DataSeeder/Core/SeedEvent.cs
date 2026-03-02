using Opossum.Core;

namespace Opossum.Samples.DataSeeder.Core;

/// <summary>
/// An event prepared for seeding before a sequence position has been assigned.
/// This is the Layer 1 → Layer 2 boundary type produced by generators.
/// </summary>
public record SeedEvent(DomainEvent Event, Metadata Metadata);
