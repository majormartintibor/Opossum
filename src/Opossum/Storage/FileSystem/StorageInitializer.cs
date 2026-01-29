using Opossum.Configuration;

namespace Opossum.Storage.FileSystem;

/// <summary>
/// Initializes the file system storage structure for the event store
/// </summary>
internal sealed class StorageInitializer
{
    private readonly OpossumOptions _options;

    /// <summary>
    /// Creates a new instance of StorageInitializer
    /// </summary>
    /// <param name="options">Configuration options containing root path and contexts</param>
    /// <exception cref="ArgumentNullException">Thrown when options is null</exception>
    public StorageInitializer(OpossumOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Initializes the storage directory structure for all configured contexts.
    /// Creates the following structure for each context:
    /// /RootPath
    ///   /ContextName
    ///     .ledger
    ///     /Events
    ///     /Indices
    ///       /EventType
    ///       /Tags
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when no contexts are configured</exception>
    /// <exception cref="IOException">Thrown when directory creation fails</exception>
    public void Initialize()
    {
        if (_options.Contexts.Count == 0)
        {
            throw new InvalidOperationException(
                "Cannot initialize storage: no contexts configured. " +
                "Use options.AddContext(\"ContextName\") to add at least one context.");
        }

        // Create root directory if it doesn't exist
        EnsureDirectoryExists(_options.RootPath);

        // Initialize each context
        foreach (var contextName in _options.Contexts)
        {
            InitializeContext(contextName);
        }
    }

    /// <summary>
    /// Initializes the directory structure for a single context
    /// </summary>
    private void InitializeContext(string contextName)
    {
        var contextPath = Path.Combine(_options.RootPath, contextName);

        // Create context directory
        EnsureDirectoryExists(contextPath);

        // Create .ledger file if it doesn't exist
        var ledgerPath = Path.Combine(contextPath, ".ledger");
        EnsureLedgerFileExists(ledgerPath);

        // Create Events directory
        var eventsPath = Path.Combine(contextPath, "Events");
        EnsureDirectoryExists(eventsPath);

        // Create Indices directory
        var indicesPath = Path.Combine(contextPath, "Indices");
        EnsureDirectoryExists(indicesPath);

        // Create EventType index directory
        var eventTypeIndexPath = Path.Combine(indicesPath, "EventType");
        EnsureDirectoryExists(eventTypeIndexPath);

        // Create Tags index directory
        var tagsIndexPath = Path.Combine(indicesPath, "Tags");
        EnsureDirectoryExists(tagsIndexPath);
    }

    /// <summary>
    /// Ensures a directory exists, creating it if necessary
    /// </summary>
    private static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    /// <summary>
    /// Ensures the ledger file exists, creating an empty file if necessary
    /// </summary>
    private static void EnsureLedgerFileExists(string path)
    {
        if (!File.Exists(path))
        {
            // Create an empty ledger file
            File.WriteAllText(path, string.Empty);
        }
    }

    /// <summary>
    /// Gets the full path to a context directory
    /// </summary>
    /// <param name="contextName">Name of the context</param>
    /// <returns>Full path to the context directory</returns>
    public string GetContextPath(string contextName)
    {
        return Path.Combine(_options.RootPath, contextName);
    }

    /// <summary>
    /// Gets the full path to the Events directory for a context
    /// </summary>
    /// <param name="contextName">Name of the context</param>
    /// <returns>Full path to the Events directory</returns>
    public string GetEventsPath(string contextName)
    {
        return Path.Combine(_options.RootPath, contextName, "Events");
    }

    /// <summary>
    /// Gets the full path to the ledger file for a context
    /// </summary>
    /// <param name="contextName">Name of the context</param>
    /// <returns>Full path to the .ledger file</returns>
    public string GetLedgerPath(string contextName)
    {
        return Path.Combine(_options.RootPath, contextName, ".ledger");
    }

    /// <summary>
    /// Gets the full path to the EventType index directory for a context
    /// </summary>
    /// <param name="contextName">Name of the context</param>
    /// <returns>Full path to the EventType index directory</returns>
    public string GetEventTypeIndexPath(string contextName)
    {
        return Path.Combine(_options.RootPath, contextName, "Indices", "EventType");
    }

    /// <summary>
    /// Gets the full path to the Tags index directory for a context
    /// </summary>
    /// <param name="contextName">Name of the context</param>
    /// <returns>Full path to the Tags index directory</returns>
    public string GetTagsIndexPath(string contextName)
    {
        return Path.Combine(_options.RootPath, contextName, "Indices", "Tags");
    }
}
