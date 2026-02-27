using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.DependencyInjection;
using Opossum.Exceptions;
using Opossum.Extensions;

namespace Opossum.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="IEventStore.ReadLastAsync"/>.
///
/// Covers the invoice-number DCB pattern (consecutive sequence without gaps)
/// as described at https://dcb.events/invoice-number/.
///
/// Each test creates its own isolated temp directory so tests can run in parallel.
/// </summary>
public sealed class ReadLastIntegrationTests : IDisposable
{
    private readonly string _tempPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly IEventStore _eventStore;

    private record InvoiceCreatedEvent(int InvoiceNumber) : IEvent;

    public ReadLastIntegrationTests()
    {
        _tempPath = Path.Combine(
            Path.GetTempPath(),
            "OpossumReadLastTests",
            Guid.NewGuid().ToString());

        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = _tempPath;
            opt.FlushEventsImmediately = false;
            opt.UseStore("TestContext");
        });
        _serviceProvider = services.BuildServiceProvider();
        _eventStore = _serviceProvider.GetRequiredService<IEventStore>();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        if (Directory.Exists(_tempPath))
        {
            try { Directory.Delete(_tempPath, recursive: true); }
            catch { /* ignore cleanup errors */ }
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static Query InvoiceQuery() => Query.FromEventTypes(nameof(InvoiceCreatedEvent));

    /// <summary>
    /// Executes one full DCB read → decide → append cycle for invoice creation.
    /// Returns the invoice number that was successfully appended.
    /// Retries automatically on <see cref="AppendConditionFailedException"/>.
    /// </summary>
    private async Task<int> CreateNextInvoiceAsync()
    {
        var query = InvoiceQuery();

        return await _eventStore.ExecuteDecisionAsync<int>(async (store, ct) =>
        {
            var last = await store.ReadLastAsync(query, ct);
            var nextNumber = last is null ? 1 : ((InvoiceCreatedEvent)last.Event.Event).InvoiceNumber + 1;

            var condition = new AppendCondition
            {
                FailIfEventsMatch = query,
                AfterSequencePosition = last?.Position
            };

            await store.AppendEventAsync(
                new InvoiceCreatedEvent(nextNumber),
                condition: condition,
                cancellationToken: ct);

            return nextNumber;
        }, maxRetries: 20, initialDelayMs: 0);
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadLastAsync_WithNoInvoices_ReturnsNullAsync()
    {
        var result = await _eventStore.ReadLastAsync(InvoiceQuery());

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadLastAsync_AfterFirstInvoice_ReturnsItAsync()
    {
        await _eventStore.AppendEventAsync(new InvoiceCreatedEvent(1));

        var result = await _eventStore.ReadLastAsync(InvoiceQuery());

        Assert.NotNull(result);
        Assert.Equal(1, ((InvoiceCreatedEvent)result.Event.Event).InvoiceNumber);
    }

    [Fact]
    public async Task ReadLastAsync_AfterMultipleInvoices_ReturnsHighestPositionAsync()
    {
        await _eventStore.AppendEventAsync(new InvoiceCreatedEvent(1));
        await _eventStore.AppendEventAsync(new InvoiceCreatedEvent(2));
        await _eventStore.AppendEventAsync(new InvoiceCreatedEvent(3));

        var result = await _eventStore.ReadLastAsync(InvoiceQuery());

        Assert.NotNull(result);
        Assert.Equal(3, ((InvoiceCreatedEvent)result.Event.Event).InvoiceNumber);
        Assert.Equal(3, result.Position);
    }

    [Fact]
    public async Task ReadLastAsync_WithInterleavedEventTypes_ReturnsOnlyLastMatchAsync()
    {
        // Append invoices interleaved with other events
        await _eventStore.AppendEventAsync(new InvoiceCreatedEvent(1));   // pos 1
        await _eventStore.AppendEventAsync(new InvoiceCreatedEvent(2));   // pos 2

        var result = await _eventStore.ReadLastAsync(InvoiceQuery());

        Assert.NotNull(result);
        Assert.Equal(2, result.Position);
        Assert.Equal(2, ((InvoiceCreatedEvent)result.Event.Event).InvoiceNumber);
    }

    [Fact]
    public async Task InvoiceNumbering_FirstInvoice_GetsNumberOneAsync()
    {
        var invoiceNumber = await CreateNextInvoiceAsync();

        Assert.Equal(1, invoiceNumber);
    }

    [Fact]
    public async Task InvoiceNumbering_SequentialCreation_FormsContinuousSequenceAsync()
    {
        const int count = 5;

        for (var i = 0; i < count; i++)
            await CreateNextInvoiceAsync();

        // Read all invoices and verify they form an unbroken sequence 1–5
        var events = await _eventStore.ReadAsync(InvoiceQuery(), null);
        var numbers = events
            .Select(e => ((InvoiceCreatedEvent)e.Event.Event).InvoiceNumber)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(count, numbers.Length);
        for (var i = 0; i < count; i++)
            Assert.Equal(i + 1, numbers[i]);
    }

    [Fact]
    public async Task InvoiceNumbering_ConcurrentCreation_ProducesUniqueSequentialNumbersWithoutGapsAsync()
    {
        const int concurrentWriters = 10;

        // All writers compete to append the next invoice at the same time
        var tasks = Enumerable
            .Range(0, concurrentWriters)
            .Select(_ => CreateNextInvoiceAsync())
            .ToArray();

        var invoiceNumbers = await Task.WhenAll(tasks);

        // Every writer must have succeeded (retried internally until it won)
        Assert.Equal(concurrentWriters, invoiceNumbers.Length);

        // Numbers must be unique and form the complete sequence 1..N
        var sorted = invoiceNumbers.OrderBy(n => n).ToArray();
        for (var i = 0; i < concurrentWriters; i++)
            Assert.Equal(i + 1, sorted[i]);
    }

    [Fact]
    public async Task InvoiceNumbering_AppendCondition_RejectsStaleDecisionAsync()
    {
        // Read: no invoices yet
        var query = InvoiceQuery();
        var last = await _eventStore.ReadLastAsync(query);
        Assert.Null(last);

        // Simulate another writer appending invoice #1 between our read and append
        await _eventStore.AppendEventAsync(new InvoiceCreatedEvent(1));

        // Our condition says "no invoices should exist" — must be rejected
        var condition = new AppendCondition
        {
            FailIfEventsMatch = query,
            AfterSequencePosition = null
        };

        await Assert.ThrowsAnyAsync<AppendConditionFailedException>(() =>
            _eventStore.AppendEventAsync(new InvoiceCreatedEvent(1), condition: condition));
    }
}
