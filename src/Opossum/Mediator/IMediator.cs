namespace Opossum.Mediator;

/// <summary>
/// Entry point for processing messages with the mediator pattern
/// </summary>
public interface IMediator
{
    /// <summary>
    /// Execute the message handling for this message synchronously and wait for the response.
    /// The message is handled locally and delegates immediately to the appropriate handler.
    /// </summary>
    /// <typeparam name="T">The expected response type</typeparam>
    /// <param name="message">The message/request to process</param>
    /// <param name="cancellation">Cancellation token for async operations</param>
    /// <param name="timeout">Optional timeout for the operation</param>
    /// <returns>The response of type T from the handler</returns>
    Task<T> InvokeAsync<T>(object message, CancellationToken cancellation = default, TimeSpan? timeout = default);
}
