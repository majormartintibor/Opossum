namespace Opossum.Storage.FileSystem;

internal sealed partial class FileSystemEventStore : IEventStoreAdmin
{
    public async Task DeleteStoreAsync(CancellationToken cancellationToken = default)
    {
        if (_options.StoreName is null)
            throw new InvalidOperationException("No store configured.");

        var contextPath = GetContextPath(_options.StoreName);

        // Fast-path: no lock needed when the directory is already absent.
        if (!Directory.Exists(contextPath))
            return;

        await _appendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check inside the lock: a concurrent DeleteStoreAsync may have already
            // removed the directory between the fast-path check above and lock acquisition.
            if (!Directory.Exists(contextPath))
                return;

            // Strip the read-only attribute from every file before deletion so that
            // write-protected event and projection files can be removed.
            foreach (var file in Directory.GetFiles(contextPath, "*", SearchOption.AllDirectories))
            {
                var attrs = File.GetAttributes(file);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }

            Directory.Delete(contextPath, recursive: true);
        }
        finally
        {
            _appendLock.Release();
        }
    }
}
