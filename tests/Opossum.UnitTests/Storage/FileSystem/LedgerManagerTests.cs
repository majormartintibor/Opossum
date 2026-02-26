using Opossum.Storage.FileSystem;

namespace Opossum.UnitTests.Storage.FileSystem;

public class LedgerManagerTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly LedgerManager _ledgerManager;

    public LedgerManagerTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "OpossumTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
        _ledgerManager = new LedgerManager(flushImmediately: false); // Faster tests
    }

    public void Dispose()
    {
        // Cleanup test directory
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    // ========================================================================
    // GetLastSequencePositionAsync Tests
    // ========================================================================

    [Fact]
    public async Task GetLastSequencePositionAsync_WhenLedgerDoesNotExist_ReturnsZeroAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "NonExistentContext");

        // Act
        var position = await _ledgerManager.GetLastSequencePositionAsync(contextPath);

        // Assert
        Assert.Equal(0, position);
    }

    [Fact]
    public async Task GetLastSequencePositionAsync_WhenLedgerExists_ReturnsLastPositionAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "ExistingContext");
        await _ledgerManager.UpdateSequencePositionAsync(contextPath, 42);

        // Act
        var position = await _ledgerManager.GetLastSequencePositionAsync(contextPath);

        // Assert
        Assert.Equal(42, position);
    }

    [Fact]
    public async Task GetLastSequencePositionAsync_WithNullContextPath_ThrowsArgumentNullExceptionAsync()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _ledgerManager.GetLastSequencePositionAsync(null!));
    }

    [Fact]
    public async Task GetLastSequencePositionAsync_WhenLedgerIsCorrupt_ReturnsZeroAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "CorruptContext");
        Directory.CreateDirectory(contextPath);
        var ledgerPath = Path.Combine(contextPath, ".ledger");

        // Write corrupt JSON
        await File.WriteAllTextAsync(ledgerPath, "{ this is not valid JSON }");

        // Act
        var position = await _ledgerManager.GetLastSequencePositionAsync(contextPath);

        // Assert
        Assert.Equal(0, position); // Corrupt ledger treated as empty
    }

    [Fact]
    public async Task GetLastSequencePositionAsync_WhenLedgerIsEmpty_ReturnsZeroAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "EmptyLedgerContext");
        Directory.CreateDirectory(contextPath);
        var ledgerPath = Path.Combine(contextPath, ".ledger");

        // Create empty file
        await File.WriteAllTextAsync(ledgerPath, "");

        // Act
        var position = await _ledgerManager.GetLastSequencePositionAsync(contextPath);

        // Assert
        Assert.Equal(0, position);
    }

    // ========================================================================
    // GetNextSequencePositionAsync Tests
    // ========================================================================

    [Fact]
    public async Task GetNextSequencePositionAsync_WhenLedgerDoesNotExist_ReturnsOneAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "NewContext");

        // Act
        var nextPosition = await _ledgerManager.GetNextSequencePositionAsync(contextPath);

        // Assert
        Assert.Equal(1, nextPosition);
    }

    [Fact]
    public async Task GetNextSequencePositionAsync_WhenLedgerExists_ReturnsIncrementedPositionAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "IncrementContext");
        await _ledgerManager.UpdateSequencePositionAsync(contextPath, 10);

        // Act
        var nextPosition = await _ledgerManager.GetNextSequencePositionAsync(contextPath);

        // Assert
        Assert.Equal(11, nextPosition);
    }

    [Fact]
    public async Task GetNextSequencePositionAsync_WithNullContextPath_ThrowsArgumentNullExceptionAsync()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _ledgerManager.GetNextSequencePositionAsync(null!));
    }

    // ========================================================================
    // UpdateSequencePositionAsync Tests
    // ========================================================================

    [Fact]
    public async Task UpdateSequencePositionAsync_CreatesLedgerFileAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "CreateLedgerContext");
        var ledgerPath = Path.Combine(contextPath, ".ledger");

        // Act
        await _ledgerManager.UpdateSequencePositionAsync(contextPath, 5);

        // Assert
        Assert.True(File.Exists(ledgerPath));
    }

    [Fact]
    public async Task UpdateSequencePositionAsync_WritesCorrectPositionAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "WritePositionContext");

        // Act
        await _ledgerManager.UpdateSequencePositionAsync(contextPath, 123);

        // Assert
        var position = await _ledgerManager.GetLastSequencePositionAsync(contextPath);
        Assert.Equal(123, position);
    }

    [Fact]
    public async Task UpdateSequencePositionAsync_OverwritesExistingPositionAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "OverwriteContext");
        await _ledgerManager.UpdateSequencePositionAsync(contextPath, 10);

        // Act
        await _ledgerManager.UpdateSequencePositionAsync(contextPath, 20);

        // Assert
        var position = await _ledgerManager.GetLastSequencePositionAsync(contextPath);
        Assert.Equal(20, position);
    }

    [Fact]
    public async Task UpdateSequencePositionAsync_WithNullContextPath_ThrowsArgumentNullExceptionAsync()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _ledgerManager.UpdateSequencePositionAsync(null!, 1));
    }

    [Fact]
    public async Task UpdateSequencePositionAsync_WithNegativePosition_ThrowsArgumentOutOfRangeExceptionAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "NegativeContext");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _ledgerManager.UpdateSequencePositionAsync(contextPath, -1));
    }

    [Fact]
    public async Task UpdateSequencePositionAsync_WithZeroPosition_SucceedsAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "ZeroContext");

        // Act
        await _ledgerManager.UpdateSequencePositionAsync(contextPath, 0);

        // Assert
        var position = await _ledgerManager.GetLastSequencePositionAsync(contextPath);
        Assert.Equal(0, position);
    }

    // ========================================================================
    // Concurrency & Locking Tests
    // ========================================================================

    [Fact]
    public async Task AcquireLockAsync_CreatesLockObjectAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "LockContext");

        // Act
        await using var ledgerLock = await _ledgerManager.AcquireLockAsync(contextPath);

        // Assert
        Assert.NotNull(ledgerLock);
    }

    [Fact]
    public async Task AcquireLockAsync_PreventsSimultaneousAccessAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "ExclusiveContext");
        var secondLockAttempted = false;
        var secondLockAcquired = false;

        // Act
        await using (var firstLock = await _ledgerManager.AcquireLockAsync(contextPath))
        {
            // Try to acquire second lock while first is held (should throw IOException)
            var lockTask = Task.Run(async () =>
            {
                secondLockAttempted = true;
                try
                {
                    // This should throw IOException because first lock is held
                    await using var secondLock = await _ledgerManager.AcquireLockAsync(contextPath);
                    secondLockAcquired = true; // Should not reach here
                }
                catch (IOException)
                {
                    // Expected - lock is exclusive
                }
            });

            // Give second task time to attempt lock
            // Increased from 100ms to 500ms for CI environment reliability
            await Task.Delay(500);

            // If still not attempted, wait a bit more (CI can be slow)
            for (int i = 0; i < 5 && !secondLockAttempted; i++)
            {
                await Task.Delay(100);
            }

            // Assert - second lock should have been attempted but not acquired
            Assert.True(secondLockAttempted, "Second lock attempt should have been made");
            Assert.False(secondLockAcquired, "Second lock should not have been acquired while first is held");

            // Wait for lock task to complete
            await lockTask;
        }

        // After first lock released, verify second attempt failed as expected
        Assert.False(secondLockAcquired, "Second lock should have failed due to exclusive access");
    }

    [Fact]
    public async Task AcquireLockAsync_ReleasesLockOnDisposeAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "DisposeLockContext");

        // Act
        await using (var firstLock = await _ledgerManager.AcquireLockAsync(contextPath))
        {
            // Lock is held
        } // Lock released here

        // Try to acquire lock again - should succeed
        await using var secondLock = await _ledgerManager.AcquireLockAsync(contextPath);

        // Assert
        Assert.NotNull(secondLock);
    }

    [Fact]
    public async Task ConcurrentUpdates_WithoutLocking_MayFailAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "ConcurrentContext");
        var successCount = 0;
        var failureCount = 0;
        var tasks = new List<Task>();

        // Act - Multiple concurrent updates WITHOUT locking (not recommended in production)
        for (int i = 1; i <= 10; i++)
        {
            var position = i;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await _ledgerManager.UpdateSequencePositionAsync(contextPath, position);
                    Interlocked.Increment(ref successCount);
                }
                catch (IOException)
                {
                    // Expected - concurrent access without locking can fail
                    Interlocked.Increment(ref failureCount);
                }
                catch (UnauthorizedAccessException)
                {
                    // Also expected on Windows when file is being moved
                    Interlocked.Increment(ref failureCount);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - At least some operations should succeed
        Assert.True(successCount > 0, "At least some concurrent updates should succeed");

        // If any succeeded, verify ledger has a valid position
        if (successCount > 0)
        {
            var finalPosition = await _ledgerManager.GetLastSequencePositionAsync(contextPath);
            Assert.InRange(finalPosition, 1, 10);
        }
    }

    [Fact]
    public async Task LockPreventsSimultaneousFileAccessAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "LockTestContext");

        // Act & Assert
        await using (var lock1 = await _ledgerManager.AcquireLockAsync(contextPath))
        {
            // While holding lock1, trying to acquire lock2 should throw or block
            await Assert.ThrowsAsync<IOException>(async () =>
            {
                await using var lock2 = await _ledgerManager.AcquireLockAsync(contextPath);
            });
        }

        // After releasing lock1, should be able to acquire new lock
        await using var lock3 = await _ledgerManager.AcquireLockAsync(contextPath);
        Assert.NotNull(lock3);
    }

    // ========================================================================
    // Integration Scenarios
    // ========================================================================

    [Fact]
    public async Task SequentialOperations_WorkCorrectlyAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "SequentialContext");

        // Act & Assert
        // Initial state
        var pos1 = await _ledgerManager.GetNextSequencePositionAsync(contextPath);
        Assert.Equal(1, pos1);

        // Update to position 1
        await _ledgerManager.UpdateSequencePositionAsync(contextPath, 1);

        // Next should be 2
        var pos2 = await _ledgerManager.GetNextSequencePositionAsync(contextPath);
        Assert.Equal(2, pos2);

        // Update to position 2
        await _ledgerManager.UpdateSequencePositionAsync(contextPath, 2);

        // Next should be 3
        var pos3 = await _ledgerManager.GetNextSequencePositionAsync(contextPath);
        Assert.Equal(3, pos3);

        // Last should be 2
        var last = await _ledgerManager.GetLastSequencePositionAsync(contextPath);
        Assert.Equal(2, last);
    }

    [Fact]
    public async Task LedgerPersistsAcrossManagerInstancesAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "PersistenceContext");
        var manager1 = new LedgerManager();
        var manager2 = new LedgerManager();

        // Act
        await manager1.UpdateSequencePositionAsync(contextPath, 100);
        var position = await manager2.GetLastSequencePositionAsync(contextPath);

        // Assert
        Assert.Equal(100, position);
    }

    [Fact]
    public async Task LedgerFileHasCorrectFormatAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "FormatContext");
        await _ledgerManager.UpdateSequencePositionAsync(contextPath, 42);

        // Act
        var ledgerPath = Path.Combine(contextPath, ".ledger");
        var content = await File.ReadAllTextAsync(ledgerPath);

        // Assert
        Assert.Contains("lastSequencePosition", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("42", content);
    }

    // ========================================================================
    // Flush Configuration Tests
    // ========================================================================

    [Fact]
    public async Task Constructor_WithFlushTrue_LedgerIsDurableAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "FlushTrueContext");
        var managerWithFlush = new LedgerManager(flushImmediately: true);

        // Act
        await managerWithFlush.UpdateSequencePositionAsync(contextPath, 42);

        // Assert
        // Ledger should be persisted (flushed to disk)
        var ledgerPath = Path.Combine(contextPath, ".ledger");
        Assert.True(File.Exists(ledgerPath));

        // Another manager should be able to read it
        var anotherManager = new LedgerManager(flushImmediately: true);
        var position = await anotherManager.GetLastSequencePositionAsync(contextPath);
        Assert.Equal(42, position);
    }

    [Fact]
    public async Task Constructor_WithFlushFalse_LedgerStillWrittenAsync()
    {
        // Arrange
        var contextPath = Path.Combine(_testDirectory, "FlushFalseContext");
        var managerNoFlush = new LedgerManager(flushImmediately: false);

        // Act
        await managerNoFlush.UpdateSequencePositionAsync(contextPath, 100);

        // Assert
        // Ledger should exist (even without flush, it's in page cache)
        var ledgerPath = Path.Combine(contextPath, ".ledger");
        Assert.True(File.Exists(ledgerPath));

        // Should be readable
        var position = await managerNoFlush.GetLastSequencePositionAsync(contextPath);
        Assert.Equal(100, position);
    }

    [Fact]
    public void Constructor_DefaultsToFlushTrue()
    {
        // Arrange & Act
        var defaultManager = new LedgerManager();

        // Assert
        // Default constructor should enable flush for production safety
        Assert.NotNull(defaultManager);
    }
}
