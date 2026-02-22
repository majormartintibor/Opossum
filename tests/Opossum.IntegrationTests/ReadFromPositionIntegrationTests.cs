using Opossum.Core;
using Opossum.DependencyInjection;
using Opossum.Extensions;

namespace Opossum.IntegrationTests;

/// <summary>
/// Integration tests for the <c>fromPosition</c> parameter of
/// <see cref="IEventStore.ReadAsync(Query, ReadOption[]?, long?)"/>.
///
/// Each test creates its own isolated temp directory so tests can run in parallel.
/// </summary>
public sealed class ReadFromPositionIntegrationTests : IDisposable
{
    private readonly string _tempPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly IEventStore _eventStore;

    private record OrderEvent(string OrderId) : IEvent;
    private record ShipmentEvent(string ShipmentId) : IEvent;

    public ReadFromPositionIntegrationTests()
    {
        _tempPath = Path.Combine(
            Path.GetTempPath(),
            "OpossumReadFromPositionTests",
            Guid.NewGuid().ToString());

        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = _tempPath;
            opt.FlushEventsImmediately = false;
            opt.AddContext("TestContext");
        });
        _serviceProvider = services.BuildServiceProvider();
        _eventStore = _serviceProvider.GetRequiredService<IEventStore>();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        if (Directory.Exists(_tempPath))
        {
            try
            { Directory.Delete(_tempPath, recursive: true); }
            catch { /* ignore cleanup errors */ }
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private Task AppendOrderAsync(string orderId) =>
        _eventStore.AppendEventAsync(new OrderEvent(orderId),
            tags: [new Tag { Key = "orderId", Value = orderId }]);

    private Task AppendShipmentAsync(string shipmentId) =>
        _eventStore.AppendEventAsync(new ShipmentEvent(shipmentId),
            tags: [new Tag { Key = "shipmentId", Value = shipmentId }]);

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_WithoutFromPosition_ReturnsAllEvents()
    {
        await AppendOrderAsync("o1");
        await AppendOrderAsync("o2");
        await AppendOrderAsync("o3");

        var events = await _eventStore.ReadAsync(Query.All(), null);

        Assert.Equal(3, events.Length);
    }

    [Fact]
    public async Task ReadAsync_WithFromPositionZero_ReturnsAllEvents()
    {
        await AppendOrderAsync("o1");
        await AppendOrderAsync("o2");

        var events = await _eventStore.ReadAsync(Query.All(), null, fromPosition: 0);

        Assert.Equal(2, events.Length);
    }

    [Fact]
    public async Task ReadAsync_WithFromPosition_ReturnsOnlyEventsAfterThatPosition()
    {
        // Append 5 events — positions 1 through 5
        for (int i = 1; i <= 5; i++)
            await AppendOrderAsync($"o{i}");

        // Request only events after position 3
        var events = await _eventStore.ReadAsync(Query.All(), null, fromPosition: 3);

        Assert.Equal(2, events.Length);
        Assert.All(events, e => Assert.True(e.Position > 3));
        Assert.Equal(4, events[0].Position);
        Assert.Equal(5, events[1].Position);
    }

    [Fact]
    public async Task ReadAsync_WithFromPositionAtLastEvent_ReturnsEmpty()
    {
        await AppendOrderAsync("o1");
        await AppendOrderAsync("o2");
        await AppendOrderAsync("o3");

        var lastPosition = (await _eventStore.ReadAsync(Query.All(), null)).Max(e => e.Position);

        var events = await _eventStore.ReadAsync(Query.All(), null, fromPosition: lastPosition);

        Assert.Empty(events);
    }

    [Fact]
    public async Task ReadAsync_WithFromPositionAndEventTypeFilter_ReturnsOnlyMatchingTypesAfterPosition()
    {
        await AppendOrderAsync("o1");        // pos 1 – OrderEvent
        await AppendShipmentAsync("s1");    // pos 2 – ShipmentEvent
        await AppendOrderAsync("o2");        // pos 3 – OrderEvent
        await AppendShipmentAsync("s2");    // pos 4 – ShipmentEvent
        await AppendOrderAsync("o3");        // pos 5 – OrderEvent

        var events = await _eventStore.ReadAsync(
            Query.FromEventTypes(nameof(OrderEvent)), null, fromPosition: 2);

        // Only OrderEvent events at positions > 2 → positions 3 and 5
        Assert.Equal(2, events.Length);
        Assert.Equal(3, events[0].Position);
        Assert.Equal(5, events[1].Position);
        Assert.All(events, e => Assert.Equal(nameof(OrderEvent), e.Event.EventType));
    }

    [Fact]
    public async Task ReadAsync_WithFromPositionAndTagFilter_ReturnsOnlyMatchingTagsAfterPosition()
    {
        var tag = new Tag { Key = "orderId", Value = "o-special" };

        await _eventStore.AppendEventAsync(new OrderEvent("o-special"),
            tags: [tag]);  // pos 1
        await AppendOrderAsync("o-other");  // pos 2
        await _eventStore.AppendEventAsync(new OrderEvent("o-special"),
            tags: [tag]);  // pos 3
        await _eventStore.AppendEventAsync(new OrderEvent("o-special"),
            tags: [tag]);  // pos 4

        var events = await _eventStore.ReadAsync(Query.FromTags(tag), null, fromPosition: 1);

        // Positions 3 and 4 match
        Assert.Equal(2, events.Length);
        Assert.Equal(3, events[0].Position);
        Assert.Equal(4, events[1].Position);
    }

    [Fact]
    public async Task ReadAsync_CanPollIncrementally_SimulatingProjectionDaemon()
    {
        // First batch — simulate existing events
        await AppendOrderAsync("o1"); // pos 1
        await AppendOrderAsync("o2"); // pos 2

        var firstBatch = await _eventStore.ReadAsync(Query.All(), null, fromPosition: 0);
        Assert.Equal(2, firstBatch.Length);

        var checkpoint = firstBatch.Max(e => e.Position); // 2

        // New events arrive
        await AppendOrderAsync("o3"); // pos 3
        await AppendOrderAsync("o4"); // pos 4

        // Poll for only new events using the saved checkpoint
        var secondBatch = await _eventStore.ReadAsync(Query.All(), null, fromPosition: checkpoint);

        Assert.Equal(2, secondBatch.Length);
        Assert.All(secondBatch, e => Assert.True(e.Position > checkpoint));
        Assert.Equal(3, secondBatch[0].Position);
        Assert.Equal(4, secondBatch[1].Position);
    }

    [Fact]
    public async Task ReadAsync_WithFromPositionExtensionMethod_ReturnsOnlyEventsAfterPosition()
    {
        await AppendOrderAsync("o1"); // pos 1
        await AppendOrderAsync("o2"); // pos 2
        await AppendOrderAsync("o3"); // pos 3

        // Use the convenience extension method
        var events = await _eventStore.ReadAsync(Query.All(), fromPosition: 1);

        Assert.Equal(2, events.Length);
        Assert.Equal(2, events[0].Position);
        Assert.Equal(3, events[1].Position);
    }
}
