using System.Reflection;

namespace Opossum.Mediator;

/// <summary>
/// Service for discovering message handlers using reflection
/// </summary>
public sealed class HandlerDiscoveryService
{
    private readonly List<Assembly> _assemblies = new();
    private static readonly string[] ValidMethodNames = { "Handle", "HandleAsync", "Consume", "ConsumeAsync" };
    
    public void IncludeAssembly(Assembly assembly)
    {
        if (assembly == null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }
        
        _assemblies.Add(assembly);
    }
    
    public List<(Type HandlerType, MethodInfo Method)> DiscoverHandlers()
    {
        var handlers = new List<(Type, MethodInfo)>();
        
        foreach (var assembly in _assemblies)
        {
            var handlerTypes = assembly.GetTypes()
                .Where(t => !t.IsInterface &&
                           !(t.IsAbstract && !t.IsSealed) && // Exclude abstract classes, but allow static classes (which are abstract AND sealed)
                           (t.Name.EndsWith("Handler") || 
                            t.GetCustomAttribute<MessageHandlerAttribute>() != null))
                .ToList();
            
            foreach (var handlerType in handlerTypes)
            {
                var methods = handlerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(IsValidHandlerMethod)
                    .ToList();
                
                foreach (var method in methods)
                {
                    handlers.Add((handlerType, method));
                }
            }
        }
        
        return handlers;
    }
    
    private bool IsValidHandlerMethod(MethodInfo method)
    {
        // Valid method names
        if (!ValidMethodNames.Contains(method.Name))
        {
            return false;
        }
        
        // Must have at least one parameter (the message)
        var parameters = method.GetParameters();
        if (parameters.Length == 0)
        {
            return false;
        }
        
        // First parameter is the message type
        var messageType = parameters[0].ParameterType;
        if (!IsValidMessageType(messageType))
        {
            return false;
        }
        
        return true;
    }
    
    private bool IsValidMessageType(Type type)
    {
        // Should be a concrete class or record
        return type.IsClass && !type.IsAbstract;
    }
}
