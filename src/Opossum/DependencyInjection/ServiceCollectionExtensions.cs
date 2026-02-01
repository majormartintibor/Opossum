using Opossum.Configuration;
using Opossum.Storage.FileSystem;

namespace Opossum.DependencyInjection;

/// <summary>
/// Extension methods for configuring Opossum services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Opossum event store services to the dependency injection container
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="configure">Optional configuration action for OpossumOptions</param>
    /// <returns>The service collection for chaining</returns>
    /// <exception cref="ArgumentNullException">Thrown when services is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when no contexts are configured</exception>
    public static IServiceCollection AddOpossum(
        this IServiceCollection services,
        Action<OpossumOptions>? configure = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // Create and configure options
        var options = new OpossumOptions();
        configure?.Invoke(options);

        // Validate that at least one context is configured
        if (options.Contexts.Count == 0)
        {
            throw new InvalidOperationException(
                "At least one context must be configured. " +
                "Use options.AddContext(\"ContextName\") in the configuration action.");
        }

        // Register options as singleton
        services.AddSingleton(options);

        // Initialize storage structure on disk
        var initializer = new StorageInitializer(options);
        initializer.Initialize();

        // Register storage initializer for use by other components
        services.AddSingleton(initializer);

        // Register event store implementation
        services.AddSingleton<IEventStore, FileSystemEventStore>();

        return services;
    }
}
