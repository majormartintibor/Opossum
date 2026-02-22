namespace Opossum.Mediator;

/// <summary>
/// Extension methods for registering the mediator with dependency injection
/// </summary>
public static class MediatorServiceExtensions
{
    public static IServiceCollection AddMediator(
        this IServiceCollection services,
        Action<MediatorOptions>? configure = null)
    {
        var options = new MediatorOptions();
        configure?.Invoke(options);

        // Discover handlers
        var discovery = new HandlerDiscoveryService();

        // Add calling assembly by default
        var callingAssembly = Assembly.GetCallingAssembly();
        discovery.IncludeAssembly(callingAssembly);

        // Add any additional assemblies (de-duplicate to avoid adding calling assembly twice)
        foreach (var assembly in options.Assemblies.Where(a => a != callingAssembly))
        {
            discovery.IncludeAssembly(assembly);
        }

        var discoveredHandlers = discovery.DiscoverHandlers();

        // Generate handler implementations
        var generatedHandlers = GenerateHandlers(discoveredHandlers);

        // Register mediator
        services.AddSingleton<IMediator>(sp =>
            new Mediator(sp, generatedHandlers));

        return services;
    }

    private static Dictionary<Type, IMessageHandler> GenerateHandlers(
        List<(Type HandlerType, MethodInfo Method)> discoveredHandlers)
    {
        var handlers = new Dictionary<Type, IMessageHandler>();

        foreach (var (handlerType, method) in discoveredHandlers)
        {
            var messageType = method.GetParameters()[0].ParameterType;

            // Check for duplicate handlers
            if (handlers.ContainsKey(messageType))
            {
                throw new InvalidOperationException(
                    $"Multiple handlers found for message type {messageType.FullName}. " +
                    $"Only one handler per message type is supported.");
            }

            // Create reflection-based handler
            var handler = new ReflectionMessageHandler(handlerType, method);

            handlers[messageType] = handler;
        }

        return handlers;
    }
}
