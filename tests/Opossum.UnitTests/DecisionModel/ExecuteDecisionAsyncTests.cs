using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.Exceptions;

namespace Opossum.UnitTests.DecisionModel;

/// <summary>
/// Unit tests for <see cref="DecisionModelExtensions.ExecuteDecisionAsync{TResult}"/>.
/// These tests verify the retry orchestration logic only — no file system or real event store.
/// </summary>
public class ExecuteDecisionAsyncTests
{
    // Minimal stub — the store instance is only passed through to the operation delegate.
    private sealed class StubEventStore : IEventStore
    {
        public Task AppendAsync(NewEvent[] events, AppendCondition? condition, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SequencedEvent[]> ReadAsync(Query query, ReadOption[]? readOptions, long? fromPosition = null) =>
            Task.FromResult(Array.Empty<SequencedEvent>());
        public Task<SequencedEvent?> ReadLastAsync(Query query, CancellationToken cancellationToken = default) =>
            Task.FromResult<SequencedEvent?>(null);
    }

    private static IEventStore Store => new StubEventStore();

    [Fact]
    public async Task ExecuteDecisionAsync_SucceedsOnFirstAttempt_ReturnsResultAsync()
    {
        var callCount = 0;

        var result = await Store.ExecuteDecisionAsync<int>((_, _) =>
        {
            callCount++;
            return Task.FromResult(42);
        }, initialDelayMs: 0);

        Assert.Equal(42, result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task ExecuteDecisionAsync_RetriesOnConcurrencyException_SucceedsOnSecondAttemptAsync()
    {
        var callCount = 0;

        var result = await Store.ExecuteDecisionAsync<string>((_, _) =>
        {
            callCount++;
            if (callCount < 2)
                throw new ConcurrencyException("Conflict");
            return Task.FromResult("ok");
        }, initialDelayMs: 0);

        Assert.Equal("ok", result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ExecuteDecisionAsync_RetriesOnAppendConditionFailedException_SucceedsOnSecondAttemptAsync()
    {
        var callCount = 0;

        var result = await Store.ExecuteDecisionAsync<string>((_, _) =>
        {
            callCount++;
            if (callCount < 2)
                throw new AppendConditionFailedException("Stale");
            return Task.FromResult("ok");
        }, initialDelayMs: 0);

        Assert.Equal("ok", result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ExecuteDecisionAsync_RethrowsConcurrencyException_AfterMaxRetriesAsync()
    {
        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            Store.ExecuteDecisionAsync<string>((_, _) =>
                throw new ConcurrencyException("Conflict"),
                maxRetries: 3,
                initialDelayMs: 0));
    }

    [Fact]
    public async Task ExecuteDecisionAsync_RethrowsAppendConditionFailedException_AfterMaxRetriesAsync()
    {
        await Assert.ThrowsAsync<AppendConditionFailedException>(() =>
            Store.ExecuteDecisionAsync<string>((_, _) =>
                throw new AppendConditionFailedException("Stale"),
                maxRetries: 3,
                initialDelayMs: 0));
    }

    [Fact]
    public async Task ExecuteDecisionAsync_InvokesOperationExactlyMaxRetries_BeforeRethrowingAsync()
    {
        var callCount = 0;

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            Store.ExecuteDecisionAsync<string>((_, _) =>
            {
                callCount++;
                throw new ConcurrencyException("Conflict");
            }, maxRetries: 3, initialDelayMs: 0));

        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task ExecuteDecisionAsync_NonRetriableException_PropagatesImmediatelyAsync()
    {
        var callCount = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Store.ExecuteDecisionAsync<string>((_, _) =>
            {
                callCount++;
                throw new InvalidOperationException("Not a concurrency issue");
            }, maxRetries: 3, initialDelayMs: 0));

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task ExecuteDecisionAsync_CancellationRequested_ThrowsOperationCanceledExceptionAsync()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Store.ExecuteDecisionAsync<string>(
                (_, _) => Task.FromResult("ok"),
                cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ExecuteDecisionAsync_PassesEventStoreToOperationAsync()
    {
        IEventStore? received = null;

        await Store.ExecuteDecisionAsync<bool>((store, _) =>
        {
            received = store;
            return Task.FromResult(true);
        }, initialDelayMs: 0);

        Assert.NotNull(received);
    }

    [Fact]
    public async Task ExecuteDecisionAsync_NullEventStore_ThrowsArgumentNullExceptionAsync()
    {
        IEventStore? nullStore = null;

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            nullStore!.ExecuteDecisionAsync<string>(
                (_, _) => Task.FromResult("ok")));
    }

    [Fact]
    public async Task ExecuteDecisionAsync_NullOperation_ThrowsArgumentNullExceptionAsync()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Store.ExecuteDecisionAsync<string>(null!));
    }
}
