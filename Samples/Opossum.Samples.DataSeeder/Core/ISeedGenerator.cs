namespace Opossum.Samples.DataSeeder.Core;

/// <summary>
/// Produces a batch of <see cref="SeedEvent"/> objects for one domain area.
/// Implementations are responsible for enforcing all domain invariants in pure code — no I/O.
/// </summary>
public interface ISeedGenerator
{
    /// <summary>
    /// Generates seed events for this domain area.
    /// May update <paramref name="context"/> with state needed by downstream generators.
    /// </summary>
    IReadOnlyList<SeedEvent> Generate(SeedContext context, SeedingConfiguration config);
}
