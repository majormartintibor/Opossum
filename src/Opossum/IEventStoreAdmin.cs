namespace Opossum;

/// <summary>
/// Destructive administrative operations for the event store.
/// Unlike <see cref="IEventStoreMaintenance"/>, these operations permanently remove data
/// and transparently bypass write-protection on files.
/// </summary>
public interface IEventStoreAdmin
{
    /// <summary>
    /// Permanently and irreversibly deletes all data owned by this store: events, indices,
    /// projections, checkpoints, and the ledger. Write-protected files (see
    /// <see cref="Configuration.OpossumOptions.WriteProtectEventFiles"/> and
    /// <see cref="Configuration.OpossumOptions.WriteProtectProjectionFiles"/>) are handled
    /// transparently — their read-only attribute is removed before deletion.
    ///
    /// After this call the store directory no longer exists on disk. Subsequent
    /// <see cref="IEventStore.AppendAsync"/> or <see cref="IEventStore.ReadAsync"/> calls
    /// will recreate the required directory structure automatically.
    ///
    /// Legitimate use cases:
    /// - Development and test environment cleanup
    /// - Wiping a store to start fresh after a schema change
    /// - GDPR / data-erasure compliance at the store level
    ///
    /// WARNING: This operation is irreversible. All event history is lost permanently.
    /// </summary>
    /// <param name="cancellationToken">Token that can cancel the operation.</param>
    Task DeleteStoreAsync(CancellationToken cancellationToken = default);
}
