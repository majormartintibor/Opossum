using Opossum.Configuration;
using Opossum.Core;
using Opossum.Storage.FileSystem;
using Opossum.UnitTests.Helpers;

namespace Opossum.UnitTests.Storage.FileSystem;

public class EventStoreMaintenanceTests : IDisposable
{
    private readonly OpossumOptions _options;
    private readonly FileSystemEventStore _store;
    private readonly string _tempRootPath;

    public EventStoreMaintenanceTests()
    {
        _tempRootPath = Path.Combine(Path.GetTempPath(), $"EventStoreMaintenanceTests_{Guid.NewGuid():N}");
        _options = new OpossumOptions
        {
            RootPath = _tempRootPath,
            FlushEventsImmediately = false
        };
        _options.UseStore("TestContext");
        _store = new FileSystemEventStore(_options);
    }

    public void Dispose()
    {
        TestDirectoryHelper.ForceDelete(_tempRootPath);
    }

    // ========================================================================
    // AddTagsAsync — Basic
    // ========================================================================

    [Fact]
    public async Task AddTagsAsync_AddsNewTagsToAllMatchingEvents()
    {
        // Arrange - two events of the same type, no existing tags
        await _store.AppendAsync(
        [
            CreateEvent("CourseCreated"),
            CreateEvent("CourseCreated")
        ], null);

        IEventStoreMaintenance maintenance = _store;

        // Act
        var result = await maintenance.AddTagsAsync(
            "CourseCreated",
            _ => [new Tag("region", "EU")]);

        // Assert
        Assert.Equal(2, result.TagsAdded);
        Assert.Equal(2, result.EventsProcessed);

        var events = await _store.ReadAsync(Query.All(), null);
        Assert.All(events, e => Assert.Contains(e.Event.Tags, t => t.Key == "region" && t.Value == "EU"));
    }

    [Fact]
    public async Task AddTagsAsync_OnlyAffectsEventsOfSpecifiedType()
    {
        // Arrange - mixed event types
        await _store.AppendAsync(
        [
            CreateEvent("CourseCreated"),
            CreateEvent("StudentRegistered"),
            CreateEvent("CourseCreated")
        ], null);

        IEventStoreMaintenance maintenance = _store;

        // Act
        await maintenance.AddTagsAsync("CourseCreated", _ => [new Tag("region", "EU")]);

        // Assert - only CourseCreated events have the new tag
        var all = await _store.ReadAsync(Query.All(), null);
        var courseCreated = all.Where(e => e.Event.EventType == "CourseCreated").ToArray();
        var student = all.Where(e => e.Event.EventType == "StudentRegistered").ToArray();

        Assert.All(courseCreated, e => Assert.Contains(e.Event.Tags, t => t.Key == "region"));
        Assert.All(student, e => Assert.DoesNotContain(e.Event.Tags, t => t.Key == "region"));
    }

    [Fact]
    public async Task AddTagsAsync_SkipsTagsWhoseKeyAlreadyExists()
    {
        // Arrange - event already has the "region" tag
        var evt = CreateEvent("CourseCreated");
        evt.Event = evt.Event with { Tags = [new Tag("region", "US")] };
        await _store.AppendAsync([evt], null);

        IEventStoreMaintenance maintenance = _store;

        // Act - try to add "region" again with a different value
        var result = await maintenance.AddTagsAsync(
            "CourseCreated",
            _ => [new Tag("region", "EU")]);

        // Assert - no tags added, existing tag preserved
        Assert.Equal(0, result.TagsAdded);
        Assert.Equal(1, result.EventsProcessed);

        var events = await _store.ReadAsync(Query.All(), null);
        var regionTag = Assert.Single(events[0].Event.Tags);
        Assert.Equal("US", regionTag.Value); // original value preserved
    }

    [Fact]
    public async Task AddTagsAsync_AddsOnlyNewKeysWhenEventHasSomeTags()
    {
        // Arrange - event has "courseId" but not "region"
        var evt = CreateEvent("CourseCreated");
        evt.Event = evt.Event with { Tags = [new Tag("courseId", "abc")] };
        await _store.AppendAsync([evt], null);

        IEventStoreMaintenance maintenance = _store;

        // Act - request both "courseId" (existing) and "region" (new)
        var result = await maintenance.AddTagsAsync(
            "CourseCreated",
            _ => [new Tag("courseId", "xyz"), new Tag("region", "EU")]);

        // Assert - only "region" was added, "courseId" was NOT overwritten
        Assert.Equal(1, result.TagsAdded);

        var events = await _store.ReadAsync(Query.All(), null);
        var tags = events[0].Event.Tags;
        Assert.Equal(2, tags.Count);
        Assert.Equal("abc", tags.First(t => t.Key == "courseId").Value); // unchanged
        Assert.Equal("EU", tags.First(t => t.Key == "region").Value);    // newly added
    }

    [Fact]
    public async Task AddTagsAsync_ReturnsZeroWhenNoEventsOfType()
    {
        // Arrange - no events at all
        IEventStoreMaintenance maintenance = _store;

        // Act
        var result = await maintenance.AddTagsAsync(
            "NonExistentEventType",
            _ => [new Tag("key", "value")]);

        // Assert
        Assert.Equal(0, result.TagsAdded);
        Assert.Equal(0, result.EventsProcessed);
    }

    // ========================================================================
    // AddTagsAsync — Index update
    // ========================================================================

    [Fact]
    public async Task AddTagsAsync_UpdatesTagIndex_SoNewTagQueryFindsEvents()
    {
        // Arrange - append without tags
        await _store.AppendAsync([CreateEvent("CourseCreated")], null);

        IEventStoreMaintenance maintenance = _store;

        // Act
        await maintenance.AddTagsAsync("CourseCreated", _ => [new Tag("region", "EU")]);

        // Assert - tag query must find the event via the updated index
        var events = await _store.ReadAsync(Query.FromTags([new Tag("region", "EU")]), null);
        Assert.Single(events);
        Assert.Equal("CourseCreated", events[0].Event.EventType);
    }

    [Fact]
    public async Task AddTagsAsync_EventFileContainsNewTags_AfterOperation()
    {
        // Arrange
        await _store.AppendAsync([CreateEvent("CourseCreated")], null);

        IEventStoreMaintenance maintenance = _store;
        await maintenance.AddTagsAsync("CourseCreated", _ => [new Tag("region", "EU")]);

        // Act - read directly from file via the store
        var events = await _store.ReadAsync(Query.All(), null);

        // Assert - the file was updated with the new tag
        Assert.Single(events);
        Assert.Contains(events[0].Event.Tags, t => t.Key == "region" && t.Value == "EU");
    }

    // ========================================================================
    // AddTagsAsync — tagFactory receives full SequencedEvent
    // ========================================================================

    [Fact]
    public async Task AddTagsAsync_TagFactoryReceivesFullSequencedEvent()
    {
        // Arrange - event has an existing "courseId" tag to derive from
        var evt = CreateEvent("CourseCreated");
        evt.Event = evt.Event with { Tags = [new Tag("courseId", "course-42")] };
        await _store.AppendAsync([evt], null);

        IEventStoreMaintenance maintenance = _store;

        // Act - derive a new tag from an existing tag value
        await maintenance.AddTagsAsync(
            "CourseCreated",
            seqEvt =>
            {
                var courseId = seqEvt.Event.Tags.FirstOrDefault(t => t.Key == "courseId")?.Value;
                return courseId is not null
                    ? [new Tag("derived", $"from-{courseId}")]
                    : [];
            });

        // Assert
        var events = await _store.ReadAsync(Query.All(), null);
        Assert.Contains(events[0].Event.Tags, t => t.Key == "derived" && t.Value == "from-course-42");
    }

    // ========================================================================
    // AddTagsAsync — Validation
    // ========================================================================

    [Fact]
    public async Task AddTagsAsync_WithNullTagFactory_ThrowsArgumentNullException()
    {
        IEventStoreMaintenance maintenance = _store;

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => maintenance.AddTagsAsync("CourseCreated", null!));
    }

    [Fact]
    public async Task AddTagsAsync_WithEmptyEventType_ThrowsArgumentException()
    {
        IEventStoreMaintenance maintenance = _store;

        await Assert.ThrowsAsync<ArgumentException>(
            () => maintenance.AddTagsAsync("", _ => []));
    }

    // ========================================================================
    // Helper Methods
    // ========================================================================

    private static NewEvent CreateEvent(string eventType) =>
        new()
        {
            Event = new DomainEvent
            {
                EventType = eventType,
                Event = new MaintenanceTestEvent()
            }
        };

    private sealed class MaintenanceTestEvent : IEvent;
}
