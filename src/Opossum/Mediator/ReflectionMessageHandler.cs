using System.Reflection;

namespace Opossum.Mediator;

/// <summary>
/// Reflection-based message handler implementation
/// </summary>
public sealed class ReflectionMessageHandler : IMessageHandler
{
    private readonly Type _handlerType;
    private readonly MethodInfo _method;
    private readonly bool _isStatic;
    private readonly ParameterInfo[] _parameters;
    
    public Type MessageType { get; }
    
    public ReflectionMessageHandler(Type handlerType, MethodInfo method)
    {
        _handlerType = handlerType ?? throw new ArgumentNullException(nameof(handlerType));
        _method = method ?? throw new ArgumentNullException(nameof(method));
        _isStatic = method.IsStatic;
        _parameters = method.GetParameters();
        
        MessageType = _parameters[0].ParameterType;
    }
    
    public async Task<object?> HandleAsync(
        object message, 
        IServiceProvider serviceProvider, 
        CancellationToken cancellation)
    {
        // Prepare handler instance
        object? handlerInstance = null;
        if (!_isStatic)
        {
            handlerInstance = ActivatorUtilities.CreateInstance(serviceProvider, _handlerType);
        }
        
        // Prepare method arguments
        var args = new object?[_parameters.Length];
        args[0] = message;
        
        // Resolve dependencies for additional parameters
        for (int i = 1; i < _parameters.Length; i++)
        {
            var paramType = _parameters[i].ParameterType;
            
            // Check if it's a CancellationToken
            if (paramType == typeof(CancellationToken))
            {
                args[i] = cancellation;
            }
            else
            {
                args[i] = serviceProvider.GetRequiredService(paramType);
            }
        }

        // Invoke the method
        object? result;
        try
        {
            result = _method.Invoke(handlerInstance, args);
        }
        catch (TargetInvocationException ex)
        {
            // Unwrap the inner exception to preserve the original exception type
            if (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
            throw;
        }

        // Handle async results
        if (result is Task task)
        {
            await task;

            // Get the result from Task<T>
            if (_method.ReturnType.IsGenericType && 
                _method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultProperty = _method.ReturnType.GetProperty("Result");
                return resultProperty?.GetValue(task);
            }

            return null;
        }
        
        return result;
    }
}
