using Microsoft.Extensions.Options;
using Opossum.Configuration;

namespace Opossum.Projections;

/// <summary>
/// Extension methods for configuring projection services
/// </summary>
public static class ProjectionServiceCollectionExtensions
{
    /// <summary>
    /// Adds projection services to the dependency injection container
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Optional configuration action for ProjectionOptions</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddProjections(
        this IServiceCollection services,
        Action<ProjectionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register options validator
        services.AddSingleton<IValidateOptions<ProjectionOptions>, ProjectionOptionsValidator>();

        // Create and configure options
        var options = new ProjectionOptions();
        configure?.Invoke(options);

        // Manually validate options immediately (fail fast)
        var validator = new ProjectionOptionsValidator();
        var validationResult = validator.Validate(null, options);
        if (validationResult.Failed)
        {
            throw new OptionsValidationException(
                nameof(ProjectionOptions),
                typeof(ProjectionOptions),
                validationResult.Failures);
        }

        // Register options as singleton
        services.AddSingleton(options);

        // Auto-discover and register projections from configured assemblies
        if (options.ScanAssemblies.Count > 0)
        {
            RegisterProjectionsFromAssemblies(services, options);
        }

        // Register projection manager
        services.AddSingleton<IProjectionManager, ProjectionManager>();

        // Register projection daemon
        services.AddHostedService<ProjectionDaemon>();

        // Register the initialization service
        services.AddHostedService<ProjectionInitializationService>();

        return services;
    }

    private static void RegisterProjectionsFromAssemblies(
        IServiceCollection services,
        ProjectionOptions options)
    {
        foreach (var assembly in options.ScanAssemblies)
        {
            var projectionTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t => t.GetCustomAttribute<ProjectionDefinitionAttribute>() != null)
                .Where(t => t.GetInterfaces().Any(i =>
                    i.IsGenericType &&
                    i.GetGenericTypeDefinition() == typeof(IProjectionDefinition<>)));

            foreach (var projectionType in projectionTypes)
            {
                try
                {
                    // Create instance using parameterless constructor
                    var instance = Activator.CreateInstance(projectionType);

                    if (instance == null)
                    {
                        continue;
                    }

                    // Find the IProjectionDefinition<TState> interface
                    var projectionInterface = projectionType.GetInterfaces()
                        .First(i => i.IsGenericType &&
                                    i.GetGenericTypeDefinition() == typeof(IProjectionDefinition<>));

                    var stateType = projectionInterface.GetGenericArguments()[0];

                    // Get the projection name
                    var nameProperty = projectionType.GetProperty(nameof(IProjectionDefinition<object>.ProjectionName));
                    var projectionName = nameProperty?.GetValue(instance) as string ?? stateType.Name;

                    // Register the projection definition as singleton
                    var definitionInterfaceType = typeof(IProjectionDefinition<>).MakeGenericType(stateType);
                    services.AddSingleton(definitionInterfaceType, instance);

                    // Check for ProjectionTags attribute and register tag provider
                    var tagsAttribute = projectionType.GetCustomAttribute<ProjectionTagsAttribute>();
                    Type? tagProviderType = null;

                    if (tagsAttribute != null)
                    {
                        tagProviderType = tagsAttribute.TagProviderType;

                        // Store tag provider type in options for later use
                        options.TagProviders[projectionName] = tagProviderType;

                        // Register tag provider as singleton in DI
                        var tagProviderInterface = typeof(IProjectionTagProvider<>).MakeGenericType(stateType);
                        services.AddSingleton(tagProviderInterface, tagProviderType);
                    }

                    // Register the projection store for this specific state type using a factory
                    var storeInterfaceType = typeof(IProjectionStore<>).MakeGenericType(stateType);
                    var capturedProjectionName = projectionName; // Capture for closure
                    var capturedStateType = stateType; // Capture for closure
                    var capturedTagProviderType = tagProviderType; // Capture for closure

                    services.AddSingleton(storeInterfaceType, sp =>
                    {
                        var opts = sp.GetRequiredService<OpossumOptions>();
                        var storeType = typeof(FileSystemProjectionStore<>).MakeGenericType(capturedStateType);

                        // Get tag provider instance if registered
                        object? tagProvider = null;
                        if (capturedTagProviderType != null)
                        {
                            var tagProviderInterfaceType = typeof(IProjectionTagProvider<>).MakeGenericType(capturedStateType);
                            tagProvider = sp.GetService(tagProviderInterfaceType);
                        }

                        return Activator.CreateInstance(storeType, opts, capturedProjectionName, tagProvider)!;
                    });
                }
                catch (Exception)
                {
                    // Skip projections that can't be instantiated
                    continue;
                }
            }
        }
    }
}

/// <summary>
/// Background service that initializes projections on startup
/// </summary>
public sealed class ProjectionInitializationService : IHostedService
{
    private readonly IProjectionManager _projectionManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly ProjectionOptions _options;

    public ProjectionInitializationService(
        IProjectionManager projectionManager,
        IServiceProvider serviceProvider,
        ProjectionOptions options)
    {
        _projectionManager = projectionManager;
        _serviceProvider = serviceProvider;
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        RegisterDiscoveredProjections();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void RegisterDiscoveredProjections()
    {
        foreach (var assembly in _options.ScanAssemblies)
        {
            var projectionTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t => t.GetCustomAttribute<ProjectionDefinitionAttribute>() != null)
                .Where(t => t.GetInterfaces().Any(i =>
                    i.IsGenericType &&
                    i.GetGenericTypeDefinition() == typeof(IProjectionDefinition<>)));

            foreach (var projectionType in projectionTypes)
            {
                try
                {
                    // Find the IProjectionDefinition<TState> interface
                    var projectionInterface = projectionType.GetInterfaces()
                        .First(i => i.IsGenericType &&
                                    i.GetGenericTypeDefinition() == typeof(IProjectionDefinition<>));

                    var stateType = projectionInterface.GetGenericArguments()[0];

                    // Get the projection definition from DI
                    var definitionInterfaceType = typeof(IProjectionDefinition<>).MakeGenericType(stateType);
                    var definition = _serviceProvider.GetRequiredService(definitionInterfaceType);

                    // Call RegisterProjection<TState>
                    var registerMethod = typeof(IProjectionManager)
                        .GetMethod(nameof(IProjectionManager.RegisterProjection))!
                        .MakeGenericMethod(stateType);

                    registerMethod.Invoke(_projectionManager, new[] { definition });
                }
                catch (Exception)
                {
                    // Skip projections that can't be instantiated
                    continue;
                }
            }
        }
    }
}

