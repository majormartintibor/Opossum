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

        // Register options validator
        services.AddSingleton<IValidateOptions<OpossumOptions>, OpossumOptionsValidator>();

        // Create and configure options
        var options = new OpossumOptions();
        configure?.Invoke(options);

        // Validate that at least one context is configured
        // MVP LIMITATION: Only the first context is actually used
        // See docs/limitations/mvp-single-context.md for details
        if (options.Contexts.Count == 0)
        {
            throw new InvalidOperationException(
                "At least one context must be configured. " +
                "Use options.AddContext(\"ContextName\") in the configuration action.");
        }

        // TODO: Add validation to enforce single context in MVP
        // if (options.Contexts.Count > 1)
        // {
        //     throw new InvalidOperationException(
        //         "MVP currently supports only ONE context. " +
        //         "See docs/limitations/mvp-single-context.md for details.");
        // }

        // Manually validate options immediately (fail fast)
        var validator = new OpossumOptionsValidator();
        var validationResult = validator.Validate(null, options);
        if (validationResult.Failed)
        {
            throw new OptionsValidationException(
                nameof(OpossumOptions),
                typeof(OpossumOptions),
                validationResult.Failures);
        }

        // Register options as singleton
        services.AddSingleton(options);

        // Initialize storage structure on disk
        var initializer = new StorageInitializer(options);
        initializer.Initialize();

        // Register storage initializer for use by other components
        services.AddSingleton(initializer);

        // Register the concrete implementation once; expose both public interfaces
        // as aliases so consumers can inject either IEventStore or IEventStoreMaintenance
        // while sharing the same singleton instance.
        services.AddSingleton<FileSystemEventStore>();
        services.AddSingleton<IEventStore>(sp => sp.GetRequiredService<FileSystemEventStore>());
        services.AddSingleton<IEventStoreMaintenance>(sp => sp.GetRequiredService<FileSystemEventStore>());

        return services;
    }
}
