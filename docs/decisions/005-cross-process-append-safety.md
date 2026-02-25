# ADR-005: Cross-Process Append Safety via Exclusive File Lock

**Date:** 2026  
**Status:** ✅ Accepted — Implemented in 0.4.0

---

## Context

### The deployment profile this decision targets

Opossum is designed for SMB, dealership, POS, and field-service applications where
multiple workstations (typically 1–12) run the same application and share one store
directory on a network drive or a mapped UNC path. This is a core use case — not an
edge case — and it is currently unsafe.

### The exact failure mode

`FileSystemEventStore.AppendAsync` uses a `SemaphoreSlim(1, 1)` to serialise
concurrent appends. A `SemaphoreSlim` is a **process-local** primitive. When two
application instances on separate machines both call `AppendAsync` simultaneously, each
acquires its own semaphore independently, and the following race becomes possible:

```
PC1  SemaphoreSlim.WaitAsync()        ← acquires PC1-local semaphore
PC2  SemaphoreSlim.WaitAsync()        ← acquires PC2-local semaphore (independent)

PC1  ReadLedger          → lastPos = 5
PC2  ReadLedger          → lastPos = 5   ← reads before PC1 writes

PC1  ValidateCondition   → no conflict (index unchanged)
PC2  ValidateCondition   → no conflict (index unchanged, same snapshot)

PC1  WriteEvent          → file position_6.json
PC2  WriteEvent          → file position_6.json  ← overwrites; one event silently lost

PC1  UpdateLedger        → 6
PC2  UpdateLedger        → 6            ← ledger is "correct", data is gone
```

The existing retry logic in `LedgerManager.AtomicMoveWithRetryAsync` and
`GetLastSequencePositionAsync` only defends against transient I/O contention — not
against this logical read-check-write race. Two processes can both successfully complete
the entire `AppendAsync` flow and still produce duplicate sequence positions.

This is not a theoretical concern: it is a certainty any time two PCs submit a form
within the same ~10ms window (the duration of a single `FlushEventsImmediately = true`
append).

---

## Decision

Introduce a **dedicated `.store.lock` file in the context directory**, opened with
`FileShare.None` for the full duration of every append operation. This file acts as
a cross-process mutual exclusion token.

The combined lock strategy becomes:

```
SemaphoreSlim.WaitAsync()          ← existing; fast within-process gate
  Acquire .store.lock (FileShare.None) with retry+backoff ← new; cross-process gate
    ValidateAppendCondition
    ReadLedger
    WriteEventFiles
    UpdateIndexes
    UpdateLedger
  Release .store.lock (close FileStream handle)
SemaphoreSlim.Release()
```

The `SemaphoreSlim` is kept because it prevents all threads within the same process
from simultaneously contesting the OS file lock. Without it, a process with 8 concurrent
callers would produce 7 redundant `IOException` / retry cycles against the lock file on
every append — needless SMB traffic.

### Why this approach works on Windows SMB

Windows SMB enforces `FileShare.None` **server-side, across all machines on the network**.
When PC1 holds the lock file open, the SMB server itself rejects PC2's open attempt with
`ERROR_SHARING_VIOLATION (0x80070020)`. This guarantee comes from the SMB server, not
from PC1's cooperation. It holds even if PC1 is under load, paused in a debugger, or
handling another request.

The OS releases all file handles owned by a process when that process terminates — even
on unclean exit. There is no stale-lock scenario. A process that crashes mid-append
releases the lock file handle automatically.

### Performance impact

| Path | Lock overhead | Total append time | Impact |
|------|--------------|-------------------|--------|
| Local drive | ~0.1 ms (one kernel syscall) | ~10 ms | < 1% |
| SMB share (LAN) | ~1–4 ms (one network round-trip) | ~12–14 ms | ~10–30% |

At the actual event rates of the target use cases (< 2 events/sec across all PCs), this
overhead is completely imperceptible to users. At 12 PCs each submitting a form every
30 seconds, the peak rate is ~0.4 events/sec — the lock is uncontested in the vast
majority of appends.

---

## Alternatives Considered and Rejected

### Named `System.Threading.Mutex`

```csharp
new Mutex(false, $"Global\\Opossum_{storeHash}")
```

Named mutexes on Windows exist in the local machine's **kernel object namespace**. Two
machines each create a mutex with the same name but they are two independent kernel
objects that have no awareness of each other. This would solve same-machine multi-process
(e.g., IIS worker recycling with overlap) but does nothing for the multi-PC scenario.
Rejected.

### Using the ledger file itself as the lock

Hold the ledger file open with `FileShare.None` for the entire append, eliminating the
temp-file-rename strategy. Rejected because:
- The temp+rename write path was specifically designed to prevent ledger corruption on
  crash mid-write. Replacing it with a direct exclusive hold removes that guarantee.
- Readers use `FileShare.Read` on the ledger. Holding it exclusively for the full append
  duration (~10 ms) blocks all concurrent reads, degrading read latency.

### SQLite as the coordination or storage backend

SQLite's own documentation explicitly states that file locking is unreliable on network
file systems (NFS, Samba). On Windows SMB the behaviour is implementation-dependent
across SMB versions. The dedicated lock file approach uses the same OS primitives that
SQLite itself would use, but without the surrounding complexity and without changing the
storage format. Rejected for coordination use; may be re-evaluated separately as a
full backend option.

### Central coordinator service

A single-process coordinator that all PCs submit appends to via HTTP or named pipe.
This reintroduces the "server to manage" problem that is Opossum's core differentiator.
Rejected.

---

## Proposed Implementation

### 1. New class: `CrossProcessLockManager`

New file: `src/Opossum/Storage/FileSystem/CrossProcessLockManager.cs`

Responsibilities:
- Acquire the `.store.lock` file with `FileShare.None`
- Retry with exponential backoff on `IOException` with sharing-violation HResult
- Throw `TimeoutException` when `CrossProcessLockTimeout` is exceeded
- Return an `IAsyncDisposable` handle that releases the lock on disposal

```csharp
internal sealed class CrossProcessLockManager
{
    private const string LockFileName = ".store.lock";
    private const int SharingViolationHResult   = unchecked((int)0x80070020); // Windows
    private const int LockViolationHResult      = unchecked((int)0x80070021); // Windows
    private readonly TimeSpan _timeout;

    internal CrossProcessLockManager(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    internal async Task<IAsyncDisposable> AcquireAsync(
        string contextPath,
        CancellationToken cancellationToken)
    {
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
                    throw new TimeoutException(
                        $"Could not acquire the cross-process store lock at '{lockPath}' " +
                        $"within {_timeout.TotalSeconds:F1}s. Another process is holding the " +
                        $"lock. Consider increasing OpossumOptions.CrossProcessLockTimeout.", ex);
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
        ex.HResult is SharingViolationHResult or LockViolationHResult;

    private sealed class FileLockHandle : IAsyncDisposable
    {
        private readonly FileStream _stream;
        internal FileLockHandle(FileStream stream) => _stream = stream;
        public ValueTask DisposeAsync() => _stream.DisposeAsync();
    }
}
```

### 2. Changes to `OpossumOptions`

Add one new property to `src/Opossum/Configuration/OpossumOptions.cs`:

```csharp
/// <summary>
/// Maximum time to wait when acquiring the cross-process append lock.
/// When multiple application instances share the same store directory
/// (e.g. via a UNC path or mapped drive), Opossum serialises appends
/// across all processes using a file-system lock. If the lock is held
/// by another process for longer than this timeout, AppendAsync throws
/// <see cref="TimeoutException"/>.
///
/// Default: 5 seconds — sufficient headroom for any business-operation
/// event rate; increase only if appends are consistently queued behind
/// large batch operations on a slow network share.
/// </summary>
public TimeSpan CrossProcessLockTimeout { get; set; } = TimeSpan.FromSeconds(5);
```

No opt-in flag. The lock acquisition on an uncontested file is a single kernel syscall
(~0.1 ms) — immeasurable against the ~10 ms append with `FlushEventsImmediately = true`.
Making it optional would add a configuration hazard with no compensating benefit.

### 3. Changes to `FileSystemEventStore`

In `FileSystemEventStore`'s constructor, instantiate the lock manager:

```csharp
private readonly CrossProcessLockManager _crossProcessLockManager;

// In constructor:
_crossProcessLockManager = new CrossProcessLockManager(options.CrossProcessLockTimeout);
```

In `AppendAsync`, wrap the existing semaphore body:

```csharp
await _appendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
try
{
    await using var _ = await _crossProcessLockManager
        .AcquireAsync(contextPath, cancellationToken)
        .ConfigureAwait(false);

    // ... existing: ValidateCondition, ReadLedger, WriteEvents, UpdateIndexes, UpdateLedger
}
finally
{
    _appendLock.Release();
}
```

The internal testing constructor also receives a `CrossProcessLockManager` parameter to
allow injection in tests that need to simulate a held lock.

---

## Testing Strategy

### Unit Tests — `Opossum.UnitTests`

All unit tests use temporary directories cleaned up after each test. No mocking.

| Test | What it verifies |
|------|-----------------|
| `AcquiresLock_WhenFileIsNotHeld` | `AcquireAsync` returns a handle; lock file exists on disk |
| `SecondAcquire_Fails_WhenLockIsHeld` | Open lock file manually with `FileShare.None`, then call `AcquireAsync` — must throw `TimeoutException` within the configured timeout |
| `ReleasesLock_OnDispose` | Acquire → dispose → acquire again — second acquisition must succeed immediately |
| `RespectsCancellationToken` | Pass an already-cancelled token — must throw `OperationCanceledException` immediately, not wait for timeout |
| `CancellationMidWait_Throws` | Acquire externally, start `AcquireAsync` with a CancellationToken, cancel mid-backoff — must throw `OperationCanceledException` |
| `ExponentialBackoff_DoesNotExceedMax` | Verify total wait time stays within `timeout + maxBackoff` tolerance |

### Integration Tests — `Opossum.IntegrationTests`

**Critical insight for testing cross-process behaviour within a single test process:**
File locking on Windows is enforced at the file-handle level, not the process level.
Two `FileSystemEventStore` instances in the same test process pointing to the same
directory will genuinely compete for the `.store.lock` file. This gives us real
lock-contention behaviour without needing to spawn child processes.

| Test | What it verifies |
|------|-----------------|
| `TwoInstances_ConcurrentAppends_ProduceContiguousPositions` | Two `FileSystemEventStore` instances on the same temp directory, 50 concurrent appends each via `Task.WhenAll`, verify all 100 positions exist with no gaps and no duplicates |
| `TwoInstances_ConcurrentAppends_NoEventOverwrite` | Same setup, verify each appended event payload is recoverable in `ReadAsync` — no overwritten files |
| `TwoInstances_AppendCondition_NoDCBViolation` | Both instances perform read→decide→append with an `AppendCondition`; verify exactly one succeeds and one gets `AppendConditionFailedException` when they compete on the same query |
| `SingleInstance_PerformanceNotDegraded` | Single-instance baseline: append 100 events sequentially, verify median latency is within 5% of the pre-lock baseline from benchmarks |
| `LockTimeout_ThrowsTimeoutException` | Configure `CrossProcessLockTimeout = 200ms`, hold lock file externally with `FileShare.None`, call `AppendAsync` — must throw `TimeoutException` within ~200ms |

### What is explicitly not tested

A true multi-process test (two separate OS processes writing to the same share
concurrently) is intentionally deferred. The file-handle-level locking guarantee from
the OS means that the single-process dual-instance tests above provide equivalent
coverage for the locking primitive. A process-level test would primarily validate
Windows SMB server behaviour, which is outside the scope of this library's test suite.

---

## Consequences

### What changes

- `CrossProcessLockManager` is a new internal class (~60 lines)
- `OpossumOptions` gains one new property with a safe default
- `FileSystemEventStore.AppendAsync` gains one `await using` around the existing lock body
- A `.store.lock` file will appear in every context directory (expected, not a backup artefact)
- `AppendAsync` on a contested SMB share adds 1–4 ms latency per call (acceptable)
- `TimeoutException` is a new exception that can surface from `AppendAsync` (must be documented in `IEventStore` XML comments)

### What does not change

- The storage format — events, indexes, ledger are all unchanged
- Read path — `ReadAsync` is unaffected
- Single-instance local performance — lock acquisition on an uncontested file is < 1 ms
- The DCB `AppendCondition` guarantees — they are now upheld across processes, not just threads
- Projections, mediator, DI registration — all unaffected
