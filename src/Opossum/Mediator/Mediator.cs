namespace Opossum.Mediator;

/// <summary>
/// Default implementation of the mediator pattern
/// </summary>
public sealed class Mediator(
    IServiceProvider serviceProvider,
    Dictionary<Type, IMessageHandler> handlers) : IMediator
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly IReadOnlyDictionary<Type, IMessageHandler> _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));

    public async Task<T> InvokeAsync<T>(
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageType = message.GetType();

        if (!_handlers.TryGetValue(messageType, out var handler))
        {
            throw new InvalidOperationException(
                $"No handler registered for message type {messageType.FullName}");
        }

        // Apply timeout if specified
        using var cts = timeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellation)
            : null;

        if (timeout.HasValue)
        {
            cts!.CancelAfter(timeout.Value);
        }

        var effectiveToken = cts?.Token ?? cancellation;

        var response = await handler.HandleAsync(message, _serviceProvider, effectiveToken).ConfigureAwait(false);

        if (response == null)
        {
            return default!;
        }

        if (response is not T typedResponse)
        {
            throw new InvalidOperationException(
                $"Handler returned type {response.GetType().FullName} but expected {typeof(T).FullName}");
        }

        return typedResponse;
    }
}
