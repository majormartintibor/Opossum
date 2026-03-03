namespace Opossum.Samples.DataSeeder.Core;

/// <summary>In-memory representation of a defined course book, used by <see cref="SeedContext"/>.</summary>
public record BookInfo(Guid BookId, Guid CourseId);
