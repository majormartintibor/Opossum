namespace Opossum.Samples.DataSeeder.Core;

/// <summary>In-memory representation of a created course, used by <see cref="SeedContext"/>.</summary>
public record CourseInfo(Guid CourseId, string Name, int MaxCapacity);
