using System.Text.Json;

namespace Opossum.Storage.FileSystem;

/// <summary>
/// Manages the ledger file for tracking sequence positions in the event store.
/// The ledger provides atomic allocation of sequence positions for event appending.
/// </summary>
internal sealed class LedgerManager
{
    private const string LedgerFileName = ".ledger";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Gets the next available sequence position for appending events.
    /// If the ledger doesn't exist, returns 1 (first position).
    /// </summary>
    /// <param name="contextPath">Path to the context directory</param>
    /// <returns>The next available sequence position</returns>
    public async Task<long> GetNextSequencePositionAsync(string contextPath)
    {
        ArgumentNullException.ThrowIfNull(contextPath);

        var lastPosition = await GetLastSequencePositionAsync(contextPath);
        return lastPosition + 1;
    }

    /// <summary>
    /// Gets the last sequence position from the ledger.
    /// If the ledger doesn't exist or is empty, returns 0.
    /// </summary>
    /// <param name="contextPath">Path to the context directory</param>
    /// <returns>The last sequence position, or 0 if no events exist</returns>
    public async Task<long> GetLastSequencePositionAsync(string contextPath)
    {
        ArgumentNullException.ThrowIfNull(contextPath);

        var ledgerPath = Path.Combine(contextPath, LedgerFileName);

        if (!File.Exists(ledgerPath))
        {
            return 0;
        }

        // Retry logic for concurrent read/write scenarios
        var maxRetries = 5;
        var retryDelay = 10;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // Use file locking to ensure thread-safe reads
                using var fileStream = new FileStream(
                    ledgerPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

                var ledgerData = await JsonSerializer.DeserializeAsync<LedgerData>(fileStream);
                return ledgerData?.LastSequencePosition ?? 0;
            }
            catch (JsonException)
            {
                // Ledger is corrupt - return 0 and let it be rebuilt
                return 0;
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                // File might be locked by a writer, wait and retry
                await Task.Delay(retryDelay);
                retryDelay *= 2;
            }
            catch (UnauthorizedAccessException) when (attempt < maxRetries - 1)
            {
                // File might be being replaced, wait and retry
                await Task.Delay(retryDelay);
                retryDelay *= 2;
            }
        }

        // Final attempt without catching IO exceptions
        try
        {
            using var fileStream = new FileStream(
                ledgerPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            var ledgerData = await JsonSerializer.DeserializeAsync<LedgerData>(fileStream);
            return ledgerData?.LastSequencePosition ?? 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Updates the ledger with a new sequence position.
    /// This operation is atomic and thread-safe.
    /// </summary>
    /// <param name="contextPath">Path to the context directory</param>
    /// <param name="position">The new sequence position to record</param>
    public async Task UpdateSequencePositionAsync(string contextPath, long position)
    {
        ArgumentNullException.ThrowIfNull(contextPath);

        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Position must be non-negative");
        }

        var ledgerPath = Path.Combine(contextPath, LedgerFileName);

        // Ensure context directory exists
        Directory.CreateDirectory(contextPath);

        var ledgerData = new LedgerData
        {
            LastSequencePosition = position,
            EventCount = position // Assuming positions start at 1
        };

        // Write atomically using a temp file and rename
        // Use a unique temp file name to avoid conflicts in concurrent scenarios
        var tempPath = ledgerPath + $".tmp.{Guid.NewGuid():N}";

        try
        {
            // Write to temp file
            using (var fileStream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                await JsonSerializer.SerializeAsync(fileStream, ledgerData, JsonOptions);
                await fileStream.FlushAsync();
            }

            // Atomic rename with retry logic to handle concurrent access
            await AtomicMoveWithRetryAsync(tempPath, ledgerPath);
        }
        catch
        {
            // Clean up temp file if something went wrong
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* Ignore cleanup errors */ }
            }
            throw;
        }
    }

    /// <summary>
    /// Atomically moves a file with retry logic to handle concurrent access.
    /// </summary>
    private static async Task AtomicMoveWithRetryAsync(string sourcePath, string destinationPath, int maxRetries = 10)
    {
        var retryDelay = 20; // Start with 20ms

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                File.Move(sourcePath, destinationPath, overwrite: true);
                return; // Success
            }
            catch (UnauthorizedAccessException) when (attempt < maxRetries - 1)
            {
                // File might be locked by a reader, wait and retry
                await Task.Delay(retryDelay);
                retryDelay *= 2; // Exponential backoff
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                // File might be in use, wait and retry
                await Task.Delay(retryDelay);
                retryDelay *= 2; // Exponential backoff
            }
        }

        // Final attempt without catching exceptions
        File.Move(sourcePath, destinationPath, overwrite: true);
    }

    /// <summary>
    /// Acquires an exclusive lock on the ledger for atomic multi-step operations.
    /// The lock is released when the returned object is disposed.
    /// </summary>
    /// <param name="contextPath">Path to the context directory</param>
    /// <returns>A disposable lock object</returns>
    public async Task<LedgerLock> AcquireLockAsync(string contextPath)
    {
        ArgumentNullException.ThrowIfNull(contextPath);

        var ledgerPath = Path.Combine(contextPath, LedgerFileName);

        // Ensure directory exists
        Directory.CreateDirectory(contextPath);

        // Ensure ledger file exists (create empty if needed)
        if (!File.Exists(ledgerPath))
        {
            // Create initial ledger file with position 0
            var initialData = new LedgerData
            {
                LastSequencePosition = 0,
                EventCount = 0
            };

            await File.WriteAllTextAsync(ledgerPath, 
                JsonSerializer.Serialize(initialData, JsonOptions));
        }

        // Open file with exclusive access
        var fileStream = new FileStream(
            ledgerPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None, // Exclusive access
            bufferSize: 4096,
            useAsync: true);

        return new LedgerLock(fileStream);
    }

    /// <summary>
    /// Represents an exclusive lock on the ledger file.
    /// </summary>
    public sealed class LedgerLock : IAsyncDisposable, IDisposable
    {
        private readonly FileStream _fileStream;

        internal LedgerLock(FileStream fileStream)
        {
            _fileStream = fileStream;
        }

        public void Dispose()
        {
            _fileStream?.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (_fileStream != null)
            {
                await _fileStream.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Internal data structure for ledger file.
    /// </summary>
    private sealed class LedgerData
    {
        public long LastSequencePosition { get; set; }
        public long EventCount { get; set; }
    }
}
