using Opossum.Storage.FileSystem;
using Opossum.UnitTests.Helpers;

namespace Opossum.UnitTests.Storage.FileSystem;

public class CrossProcessLockManagerTests : IDisposable
{
    private readonly string _testDirectory;

    public CrossProcessLockManagerTests()
    {
        _testDirectory = Path.Combine(
            Path.GetTempPath(),
            "CrossProcessLockManagerTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        TestDirectoryHelper.ForceDelete(_testDirectory);
    }

    // ========================================================================
    // Basic acquisition
    // ========================================================================

    [Fact]
    public async Task AcquiresLock_WhenFileIsNotHeld_ReturnsHandle()
    {
        var lockManager = new CrossProcessLockManager(TimeSpan.FromSeconds(1));

        await using var handle = await lockManager.AcquireAsync(_testDirectory, CancellationToken.None);

        Assert.NotNull(handle);
        Assert.True(File.Exists(Path.Combine(_testDirectory, ".store.lock")));
    }

    [Fact]
    public async Task AcquiresLock_CreatesContextDirectory_WhenItDoesNotExist()
    {
        var newDirectory = Path.Combine(_testDirectory, "NewContext");
        Assert.False(Directory.Exists(newDirectory));

        var lockManager = new CrossProcessLockManager(TimeSpan.FromSeconds(1));

        await using var handle = await lockManager.AcquireAsync(newDirectory, CancellationToken.None);

        Assert.True(Directory.Exists(newDirectory));
        Assert.True(File.Exists(Path.Combine(newDirectory, ".store.lock")));
    }

    // ========================================================================
    // Contention behaviour
    // ========================================================================

    [Fact]
    public async Task SecondAcquire_WhenLockIsHeld_ThrowsTimeoutException()
    {
        var lockManager = new CrossProcessLockManager(TimeSpan.FromMilliseconds(200));

        using var blocker = OpenLockFileExclusive(_testDirectory);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            lockManager.AcquireAsync(_testDirectory, CancellationToken.None));
    }

    [Fact]
    public async Task TimeoutException_ContainsLockPathAndConfigOption()
    {
        var lockManager = new CrossProcessLockManager(TimeSpan.FromMilliseconds(200));

        using var blocker = OpenLockFileExclusive(_testDirectory);

        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            lockManager.AcquireAsync(_testDirectory, CancellationToken.None));

        // Message must identify the lock file so the user knows where to look
        Assert.Contains(".store.lock", ex.Message);
        // Message must name the configuration option so the user knows what to increase
        Assert.Contains("CrossProcessLockTimeout", ex.Message);
    }

    // ========================================================================
    // Release on dispose
    // ========================================================================

    [Fact]
    public async Task ReleasesLock_OnDispose_AllowsReacquisition()
    {
        var lockManager = new CrossProcessLockManager(TimeSpan.FromSeconds(1));

        var handle = await lockManager.AcquireAsync(_testDirectory, CancellationToken.None);
        await handle.DisposeAsync();

        // Second acquisition must succeed immediately
        await using var handle2 = await lockManager.AcquireAsync(_testDirectory, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_testDirectory, ".store.lock")));
    }

    // ========================================================================
    // Cancellation
    // ========================================================================

    [Fact]
    public async Task AcquireAsync_WithAlreadyCancelledToken_ThrowsImmediately()
    {
        var lockManager = new CrossProcessLockManager(TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            lockManager.AcquireAsync(_testDirectory, cts.Token));
    }

    [Fact]
    public async Task AcquireAsync_CancellationDuringBackoff_ThrowsOperationCanceledException()
    {
        var lockManager = new CrossProcessLockManager(TimeSpan.FromSeconds(10));

        using var blocker = OpenLockFileExclusive(_testDirectory);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Task.Delay throws TaskCanceledException (a subtype of OperationCanceledException).
        // Use Record + IsAssignableFrom to accept any OperationCanceledException subtype.
        var ex = await Record.ExceptionAsync(() =>
            lockManager.AcquireAsync(_testDirectory, cts.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    // ========================================================================
    // Backoff timing
    // ========================================================================

    [Fact]
    public async Task ExponentialBackoff_TotalWaitTime_IsWithinExpectedBounds()
    {
        var timeout = TimeSpan.FromMilliseconds(300);
        var lockManager = new CrossProcessLockManager(timeout);

        using var blocker = OpenLockFileExclusive(_testDirectory);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(() =>
            lockManager.AcquireAsync(_testDirectory, CancellationToken.None));
        sw.Stop();

        // Must have waited at least the configured timeout (minus small scheduling slack)
        Assert.True(sw.Elapsed >= timeout - TimeSpan.FromMilliseconds(50),
            $"Waited only {sw.Elapsed.TotalMilliseconds:F0}ms; expected >= {timeout.TotalMilliseconds - 50}ms");

        // Must not have waited significantly longer than timeout + maxBackoff(500ms) + scheduling slack(200ms)
        var upperBound = timeout + TimeSpan.FromMilliseconds(700);
        Assert.True(sw.Elapsed < upperBound,
            $"Waited {sw.Elapsed.TotalMilliseconds:F0}ms; expected < {upperBound.TotalMilliseconds}ms");
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private FileStream OpenLockFileExclusive(string contextPath) =>
        new(
            Path.Combine(contextPath, ".store.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1,
            FileOptions.None);
}
