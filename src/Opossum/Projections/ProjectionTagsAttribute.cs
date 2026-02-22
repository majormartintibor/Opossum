namespace Opossum.Projections;

/// <summary>
/// Specifies the tag provider for a projection, enabling tag-based indexing and efficient queries.
/// The tag provider will be automatically discovered and registered during assembly scanning.
/// </summary>
/// <remarks>
/// Example usage:
/// <code>
/// [ProjectionDefinition("StudentShortInfo")]
/// [ProjectionTags(typeof(StudentShortInfoTagProvider))]
/// public class StudentShortInfoProjection : IProjectionDefinition&lt;StudentShortInfo&gt;
/// {
///     // ...
/// }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ProjectionTagsAttribute : Attribute
{
    /// <summary>
    /// Gets the type of the tag provider that implements IProjectionTagProvider&lt;TState&gt;
    /// </summary>
    public Type TagProviderType { get; }

    /// <summary>
    /// Initializes a new instance of the ProjectionTagsAttribute.
    /// </summary>
    /// <param name="tagProviderType">
    /// The type that implements IProjectionTagProvider&lt;TState&gt; for this projection.
    /// Must have a parameterless constructor or be resolvable from the DI container.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when tagProviderType is null</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when tagProviderType does not implement IProjectionTagProvider&lt;TState&gt;
    /// </exception>
    public ProjectionTagsAttribute(Type tagProviderType)
    {
        ArgumentNullException.ThrowIfNull(tagProviderType);

        // Validate that the type implements IProjectionTagProvider<>
        var implementsInterface = tagProviderType.GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IProjectionTagProvider<>));

        if (!implementsInterface)
        {
            throw new ArgumentException(
                $"Type '{tagProviderType.FullName}' must implement IProjectionTagProvider<TState>",
                nameof(tagProviderType));
        }

        TagProviderType = tagProviderType;
    }
}
