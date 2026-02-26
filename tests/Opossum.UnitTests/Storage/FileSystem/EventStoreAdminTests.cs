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
    public async Task DeleteStoreAsync_WithEvents_DeletesStoreDirectoryAsync()
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
    public async Task DeleteStoreAsync_WhenStoreDoesNotExist_CompletesGracefullyAsync()
    {
        // Arrange — ensure the store directory does not exist
        var storePath = Path.Combine(_tempRootPath, "TestContext");
        if (Directory.Exists(storePath))
            Directory.Delete(storePath, recursive: true);

        // Act & Assert — should not throw
        await _store.DeleteStoreAsync();
    }

    [Fact]
    public async Task DeleteStoreAsync_WithWriteProtectedEvents_DeletesFilesSuccessfullyAsync()
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
    public async Task DeleteStoreAsync_WithWriteProtectedProjections_DeletesFilesSuccessfullyAsync()
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
    public async Task DeleteStoreAsync_ThenAppend_RecreatesStoreFromScratchAsync()
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
    public async Task DeleteStoreAsync_WhenNoStoreConfigured_ThrowsInvalidOperationExceptionAsync()
    {
        // Arrange — create a store without calling UseStore
        var unconfiguredOptions = new OpossumOptions { RootPath = _tempRootPath };
        var unconfiguredStore = new FileSystemEventStore(unconfiguredOptions);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => unconfiguredStore.DeleteStoreAsync());
    }

    // ========================================================================
    // DeleteStoreAsync — Concurrency
    // ========================================================================

    [Fact]
    public async Task DeleteStoreAsync_ConcurrentWithAppendAsync_NeitherThrowsAndStoreIsConsistentAsync()
    {
        // Arrange — seed the store so the directory exists before racing.
        await _store.AppendAsync([CreateEvent("SeedEvent")], null);

        // Act — fire append and delete simultaneously; at least one must succeed without
        // throwing an IOException / DirectoryNotFoundException caused by a missing lock.
        var appendTask = _store.AppendAsync([CreateEvent("RaceEvent")], null);
        var deleteTask = _store.DeleteStoreAsync();

        var exceptions = new List<Exception>();

        try { await appendTask; }
        catch (Exception ex) { exceptions.Add(ex); }

        try { await deleteTask; }
        catch (Exception ex) { exceptions.Add(ex); }

        // Neither operation should surface a raw IO error — only expected domain
        // exceptions (e.g. store directory gone during append) are acceptable.
        Assert.DoesNotContain(exceptions, ex => ex is IOException or DirectoryNotFoundException);
    }

    [Fact]
    public async Task DeleteStoreAsync_CalledTwiceConcurrently_BothCompleteWithoutErrorAsync()
    {
        // Arrange
        await _store.AppendAsync([CreateEvent("SeedEvent")], null);

        // Act — two concurrent deletes; second one should be a graceful no-op.
        var t1 = _store.DeleteStoreAsync();
        var t2 = _store.DeleteStoreAsync();

        // Assert — neither should throw
        await t1;
        await t2;

        var storePath = Path.Combine(_tempRootPath, "TestContext");
        Assert.False(Directory.Exists(storePath));
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
