using System.Text.Json;
using Opossum.Configuration;

namespace Opossum.Projections;

/// <summary>
/// File-based projection store implementation
/// </summary>
/// <typeparam name="TState">The projection state type</typeparam>
internal sealed class FileSystemProjectionStore<TState> : IProjectionStore<TState> where TState : class
{
    private readonly string _projectionPath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public FileSystemProjectionStore(OpossumOptions options, string projectionName)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionName);

        if (options.Contexts.Count == 0)
        {
            throw new InvalidOperationException("No contexts configured");
        }

        var contextPath = Path.Combine(options.RootPath, options.Contexts[0]);
        _projectionPath = Path.Combine(contextPath, "Projections", projectionName);

        Directory.CreateDirectory(_projectionPath);
    }

    public async Task<TState?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var filePath = GetFilePath(key);
        
        if (!File.Exists(filePath))
        {
            return null;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            return JsonSerializer.Deserialize<TState>(json, _jsonOptions);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<TState>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var files = Directory.GetFiles(_projectionPath, "*.json");
        var results = new List<TState>(files.Length);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var state = JsonSerializer.Deserialize<TState>(json, _jsonOptions);
                
                if (state != null)
                {
                    results.Add(state);
                }
            }
            catch (Exception)
            {
                // Skip corrupted files
                continue;
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<TState>> QueryAsync(Func<TState, bool> predicate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var all = await GetAllAsync(cancellationToken);
        return all.Where(predicate).ToList();
    }

    public async Task SaveAsync(string key, TState state, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(state);

        var filePath = GetFilePath(key);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var json = JsonSerializer.Serialize(state, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var filePath = GetFilePath(key);

        if (!File.Exists(filePath))
        {
            return;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            File.Delete(filePath);
        }
        finally
        {
            _lock.Release();
        }
    }

    private string GetFilePath(string key)
    {
        // Sanitize key for file system
        var safeKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_projectionPath, $"{safeKey}.json");
    }
}
