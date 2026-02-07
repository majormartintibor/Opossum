namespace Opossum.BenchmarkTests.Helpers;

/// <summary>
/// Helper for managing temporary file system resources in benchmarks
/// </summary>
public class TempFileSystemHelper : IDisposable
{
    private readonly string _tempPath;
    private bool _disposed;

    public TempFileSystemHelper(string prefix = "BenchmarkTemp")
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempPath);
    }

    public string TempPath => _tempPath;

    public void Dispose()
    {
        if (_disposed) return;
        
        CleanupWithRetry(_tempPath);
        _disposed = true;
    }

    /// <summary>
    /// Cleanup with retry logic for locked files (Windows)
    /// </summary>
    private static void CleanupWithRetry(string path, int maxRetries = 5)
    {
        if (!Directory.Exists(path)) return;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (i < maxRetries - 1)
            {
                // File might be locked, wait and retry
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (i < maxRetries - 1)
            {
                // File might be locked, wait and retry
                Thread.Sleep(100);
            }
        }

        // Final attempt - if this fails, we give up
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors on final attempt
        }
    }

    /// <summary>
    /// Creates a sub-directory within the temp path
    /// </summary>
    public string CreateSubDirectory(string name)
    {
        var subPath = Path.Combine(_tempPath, name);
        Directory.CreateDirectory(subPath);
        return subPath;
    }

    /// <summary>
    /// Gets the full path for a file within the temp directory
    /// </summary>
    public string GetFilePath(string fileName)
    {
        return Path.Combine(_tempPath, fileName);
    }
}
