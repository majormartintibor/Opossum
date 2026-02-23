namespace Opossum.Storage.FileSystem;

internal sealed partial class FileSystemEventStore : IEventStoreAdmin
{
    public Task DeleteStoreAsync(CancellationToken cancellationToken = default)
    {
        if (_options.StoreName is null)
            throw new InvalidOperationException("No store configured.");

        var contextPath = GetContextPath(_options.StoreName);

        if (!Directory.Exists(contextPath))
            return Task.CompletedTask;

        // Strip the read-only attribute from every file before deletion so that
        // write-protected event and projection files can be removed.
        foreach (var file in Directory.GetFiles(contextPath, "*", SearchOption.AllDirectories))
        {
            var attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
        }

        Directory.Delete(contextPath, recursive: true);
        return Task.CompletedTask;
    }
}
