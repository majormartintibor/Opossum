using System.Reflection;

namespace Opossum.Mediator;

/// <summary>
/// Configuration options for the mediator
/// </summary>
public sealed class MediatorOptions
{
    /// <summary>
    /// Assemblies to scan for message handlers
    /// </summary>
    public List<Assembly> Assemblies { get; } = new();
}
