using Opossum.Configuration;
using Opossum.Core;
using Opossum.Storage.FileSystem;
using Opossum.UnitTests.Helpers;

namespace Opossum.UnitTests.Storage.FileSystem;

public class EventStoreAdminTests : IDisposable
{
    private readonly string _tempRootPath;
    private readonly OpossumOptions _options;
    private readonly FileSystemEventStore _store;

    public EventStoreAdminTests()
    {
        _tempRootPath = Path.Combine(Path.GetTempPath(), $"EventStoreAdminTests_{Guid.NewGuid():N}");
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
    // DeleteStoreAsync — Basic
    // ========================================================================

    [Fact]
    public async Task DeleteStoreAsync_WithEvents_DeletesStoreDirectory()
    {
        // Arrange — write one event so the directory exists
        await _store.AppendAsync([CreateEvent("TestEvent")], null);

        var storePath = Path.Combine(_tempRootPath, "TestContext");
        Assert.True(Directory.Exists(storePath));

        // Act
        await _store.DeleteStoreAsync();

        // Assert
        Assert.False(Directory.Exists(storePath));
    }

    [Fact]
    public async Task DeleteStoreAsync_WhenStoreDoesNotExist_CompletesGracefully()
    {
        // Arrange — ensure the store directory does not exist
        var storePath = Path.Combine(_tempRootPath, "TestContext");
        if (Directory.Exists(storePath))
            Directory.Delete(storePath, recursive: true);

        // Act & Assert — should not throw
        await _store.DeleteStoreAsync();
    }

    [Fact]
    public async Task DeleteStoreAsync_WithWriteProtectedEvents_DeletesFilesSuccessfully()
    {
        // Arrange — enable write protection and write an event
        var protectedOptions = new OpossumOptions
        {
            RootPath = _tempRootPath,
            FlushEventsImmediately = false,
            WriteProtectEventFiles = true
        };
        protectedOptions.UseStore("ProtectedContext");
        var protectedStore = new FileSystemEventStore(protectedOptions);

        await protectedStore.AppendAsync([CreateEvent("ProtectedEvent")], null);

        var storePath = Path.Combine(_tempRootPath, "ProtectedContext");
        Assert.True(Directory.Exists(storePath));

        // Verify the event file is actually read-only
        var eventFile = Directory.GetFiles(storePath, "*.json", SearchOption.AllDirectories).First();
        Assert.True((File.GetAttributes(eventFile) & FileAttributes.ReadOnly) != 0);

        // Act — should not throw UnauthorizedAccessException
        await protectedStore.DeleteStoreAsync();

        // Assert
        Assert.False(Directory.Exists(storePath));
    }

    [Fact]
    public async Task DeleteStoreAsync_WithWriteProtectedProjections_DeletesFilesSuccessfully()
    {
        // Arrange — enable write protection and create a protected projection file manually
        var protectedOptions = new OpossumOptions
        {
            RootPath = _tempRootPath,
            FlushEventsImmediately = false,
            WriteProtectEventFiles = true,
            WriteProtectProjectionFiles = true
        };
        protectedOptions.UseStore("ProtectedContext2");
        var protectedStore = new FileSystemEventStore(protectedOptions);

        await protectedStore.AppendAsync([CreateEvent("SomeEvent")], null);

        var projectionDir = Path.Combine(_tempRootPath, "ProtectedContext2", "Projections", "TestProjection");
        Directory.CreateDirectory(projectionDir);
        var projectionFile = Path.Combine(projectionDir, "key-1.json");
        await File.WriteAllTextAsync(projectionFile, "{}");
        File.SetAttributes(projectionFile, FileAttributes.ReadOnly);

        // Act — should not throw UnauthorizedAccessException
        await protectedStore.DeleteStoreAsync();

        // Assert
        Assert.False(Directory.Exists(Path.Combine(_tempRootPath, "ProtectedContext2")));
    }

    [Fact]
    public async Task DeleteStoreAsync_ThenAppend_RecreatesStoreFromScratch()
    {
        // Arrange — seed an event then delete
        await _store.AppendAsync([CreateEvent("OldEvent")], null);
        await _store.DeleteStoreAsync();

        // Act — append after deletion
        await _store.AppendAsync([CreateEvent("NewEvent")], null);

        // Assert — only the new event exists; sequence restarts at position 1
        var events = await _store.ReadAsync(Query.All(), null);
        Assert.Single(events);
        Assert.Equal(1, events[0].Position);
        Assert.Equal("NewEvent", events[0].Event.EventType);
    }

    [Fact]
    public async Task DeleteStoreAsync_WhenNoStoreConfigured_ThrowsInvalidOperationException()
    {
        // Arrange — create a store without calling UseStore
        var unconfiguredOptions = new OpossumOptions { RootPath = _tempRootPath };
        var unconfiguredStore = new FileSystemEventStore(unconfiguredOptions);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => unconfiguredStore.DeleteStoreAsync());
    }

    // ========================================================================
    // IEventStoreAdmin DI registration
    // ========================================================================

    [Fact]
    public void FileSystemEventStore_ImplementsIEventStoreAdmin()
    {
        Assert.IsAssignableFrom<IEventStoreAdmin>(_store);
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private static NewEvent CreateEvent(string eventType) => new()
    {
        Event = new DomainEvent
        {
            EventType = eventType,
            Event = new TestDomainEvent { Data = eventType }
        }
    };
}
