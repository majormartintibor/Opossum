namespace Opossum.Mediator;

/// <summary>
/// Interface for generated/compiled message handlers
/// </summary>
public interface IMessageHandler
{
    /// <summary>
    /// The type of message this handler processes
    /// </summary>
    Type MessageType { get; }

    /// <summary>
    /// Execute the handler logic
    /// </summary>
    /// <param name="message">The message to handle</param>
    /// <param name="serviceProvider">Service provider for dependency injection</param>
    /// <param name="cancellation">Cancellation token</param>
    /// <returns>The response object, or null if no response</returns>
    Task<object?> HandleAsync(object message, IServiceProvider serviceProvider, CancellationToken cancellation);
}
