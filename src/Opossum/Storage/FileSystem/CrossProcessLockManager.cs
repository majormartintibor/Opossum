namespace Opossum.Storage.FileSystem;

/// <summary>
/// Acquires and releases a cross-process mutual-exclusion lock backed by a dedicated
/// <c>.store.lock</c> file in the context directory.
///
/// <para>
/// On Windows (including SMB network shares), <see cref="FileShare.None"/> is enforced
/// server-side: the OS rejects a competing open attempt with ERROR_SHARING_VIOLATION
/// before the caller's code runs. This guarantee is process-agnostic — it holds across
/// all machines that share the same network path.
/// </para>
///
/// <para>
/// A crashed or cleanly-exited process releases all of its file handles automatically.
/// There is no stale-lock scenario: the OS cleans up when the process dies.
/// </para>
/// </summary>
internal sealed class CrossProcessLockManager
{
    private const string LockFileName = ".store.lock";
    private const int SharingViolationHResult = unchecked((int)0x80070020); // Windows ERROR_SHARING_VIOLATION
    private const int LockViolationHResult    = unchecked((int)0x80070021); // Windows ERROR_LOCK_VIOLATION
    // On Linux/Unix, .NET enforces FileShare.None via flock(LOCK_EX|LOCK_NB).
    // When the lock is already held, flock() returns EWOULDBLOCK and .NET throws
    // new IOException(message, rawErrno) — so HResult equals the raw POSIX errno,
    // not a Windows HRESULT. These values are the standard POSIX errno constants:
    private const int EagainLinux     = 11; // Linux   EAGAIN = EWOULDBLOCK
    private const int EwouldblockUnix = 35; // macOS/BSD EWOULDBLOCK = EAGAIN

    private readonly TimeSpan _timeout;

    internal CrossProcessLockManager(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    /// <summary>
    /// Acquires the cross-process lock for the given context directory.
    /// Retries with exponential backoff until the lock is granted or the configured
    /// timeout elapses.
    /// </summary>
    /// <param name="contextPath">The context directory that owns the lock file.</param>
    /// <param name="cancellationToken">Token that cancels the wait.</param>
    /// <returns>
    /// An <see cref="IAsyncDisposable"/> whose disposal releases the lock.
    /// </returns>
    /// <exception cref="TimeoutException">
    /// Thrown when the lock cannot be acquired within <see cref="CrossProcessLockManager._timeout"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled while waiting.
    /// </exception>
    internal async Task<IAsyncDisposable> AcquireAsync(
        string contextPath,
        CancellationToken cancellationToken)
    {
        // Ensure the context directory exists before trying to create the lock file.
        // This is a no-op on all subsequent calls and costs one kernel stat() / CreateFile.
        Directory.CreateDirectory(contextPath);

        var lockPath = Path.Combine(contextPath, LockFileName);
        var deadline = DateTimeOffset.UtcNow + _timeout;
        var backoffMs = 10;
        const int maxBackoffMs = 500;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var stream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);

                return new FileLockHandle(stream);
            }
            catch (IOException ex) when (IsLockViolation(ex))
            {
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    var timeoutSeconds = _timeout.TotalSeconds
                        .ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                    throw new TimeoutException(
                        $"Could not acquire the cross-process store lock at '{lockPath}' " +
                        $"within {timeoutSeconds}s. Another process is holding the lock. " +
                        $"Consider increasing OpossumOptions.CrossProcessLockTimeout.", ex);
                }

                var remaining = deadline - DateTimeOffset.UtcNow;
                var delay = TimeSpan.FromMilliseconds(
                    Math.Min(backoffMs, remaining.TotalMilliseconds));

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                backoffMs = Math.Min(backoffMs * 2, maxBackoffMs);
            }
        }
    }

    private static bool IsLockViolation(IOException ex) =>
        ex.HResult is SharingViolationHResult or LockViolationHResult // Windows
            or EagainLinux or EwouldblockUnix;                        // Linux / macOS / BSD

    private sealed class FileLockHandle : IAsyncDisposable
    {
        private readonly FileStream _stream;

        internal FileLockHandle(FileStream stream) => _stream = stream;

        public ValueTask DisposeAsync() => _stream.DisposeAsync();
    }
}
