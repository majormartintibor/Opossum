namespace Opossum.Projections;

/// <summary>
/// Marks a class as a projection definition for automatic discovery
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ProjectionDefinitionAttribute : Attribute
{
    /// <summary>
    /// Projection name
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Creates a projection definition attribute
    /// </summary>
    /// <param name="name">Projection name</param>
    public ProjectionDefinitionAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }
}
