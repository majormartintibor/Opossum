# Resolved Limitation: Crash-Recovery Position Collision

> **Severity:** ~~High — silent data loss possible~~ **Resolved in 0.5.0**
> **Introduced:** v0.1.0
> **Affected:** All versions up to and including 0.4.x
> **Fixed in:** 0.5.0 — see [implementation tasks](../releases/0.5.0-crash-recovery-tasks.md)

---

## Resolution (0.5.0)

This limitation was fixed in v0.5.0 via two complementary changes:

### 1. Idempotent event writes

`WriteEventAsync` now checks whether the destination file already exists before moving
the temp file into place. If the file exists and this is a normal append (not a
maintenance rewrite via `AddTagsAsync`), the write is **skipped** — the temp file is
deleted and the method returns without overwriting the existing event. This makes every
event write idempotent on restart.

The `allowOverwrite` parameter (default `false`) controls this behaviour. Only the
`AddTagsAsync` maintenance path passes `allowOverwrite: true`.

### 2. Ledger reconciliation on first append

On the first `AppendAsync` call after process startup, `LedgerManager.ReconcileLedgerAsync`
scans the events directory for the highest-numbered `*.json` file. If this position
exceeds the ledger's `LastSequencePosition`, the ledger is updated to match. This runs
inside the cross-process lock, before position allocation, and is cached so it only
executes once per process lifetime.

Together, these changes ensure that:
- Orphaned event files from a crash between step 7 and step 9 are never overwritten.
- The ledger catches up to the actual on-disk state before allocating new positions.
- New events are assigned positions **after** the orphaned ones.

---

## Original Problem (pre-0.5.0)

`AppendAsync` writes events in three sequential phases:

| Step | Operation |
|------|-----------|
| 7    | Write event files at positions N, N+1, … |
| 8    | Update index files |
| 9    | Update ledger ← position becomes "committed" only here |

If the process crashes (or is killed, or loses power) **after step 7 but before step 9**,
event files are left on disk at positions the ledger does not record. On the next
`AppendAsync`:

1. `GetNextSequencePositionAsync` reads the stale ledger and allocates the **same positions again**.
2. `WriteEventAsync` overwrites the orphaned event files with new events —
   **silently discarding the original events**.

This is a classic **write-ahead log (WAL) violation**: the data reaches disk before
the intent is recorded in the commit log, instead of the other way around.

---

## Why `WriteProtectEventFiles` Does Not Protect Against This

`WriteProtectEventFiles = true` marks event files read-only after they are written,
guarding against accidental corruption during normal operation. However, `WriteEventAsync`
explicitly strips the `ReadOnly` attribute before overwriting an existing file:

```csharp
// EventFileManager.cs
if (_writeProtect && File.Exists(filePath))
{
    var existing = File.GetAttributes(filePath);
    if ((existing & FileAttributes.ReadOnly) != 0)
        File.SetAttributes(filePath, existing & ~FileAttributes.ReadOnly);
}
File.Move(tempPath, filePath, overwrite: true);
```

This code path was introduced to support the `AddTagsAsync` maintenance operation,
which legitimately rewrites event files with additional tag metadata. The crash-recovery
overwrite follows the same path and is therefore equally unguarded.

---

## Conditions Required to Trigger

All of the following must occur simultaneously:

1. `AppendAsync` completes step 7 (event files written to disk) but not step 9
   (ledger updated).
2. The process terminates uncleanly during that window (crash, `kill -9`, power
   failure, OOM kill).
3. A new `AppendAsync` call is made on the same store after restart.

The crash window is **very short** — typically a few milliseconds per append. The risk
scales with batch size: an append of 1,000 events has a proportionally larger crash
window than a single-event append.

---

## Impact

| Scenario | Consequence |
|----------|-------------|
| Crash during single-event append | 1 event silently overwritten |
| Crash during batch append of N events | Up to N events silently overwritten |
| `WriteProtectEventFiles = true` | No protection (attribute is stripped before overwrite) |
| `FlushEventsImmediately = true` | Events survive the crash on disk but are still overwritten on restart |

---

## Manual Detection (Workaround)

There is no automatic recovery in v0.4.x. To detect a torn-write state manually after
an unclean shutdown:

1. Open the `.ledger` file in the store directory and note `LastSequencePosition`.
2. Scan the `Events/` subdirectory for files with positions **greater than** that value.
3. If such files exist, the store is in a torn-write state.
4. **Do not append to the store** until the ledger is manually reconciled by either:
   - Deleting the orphaned event files and letting the store continue from the
     last committed position, or
   - Manually updating `LastSequencePosition` in the `.ledger` to match the highest
     file on disk (only safe if you can verify the orphaned files are complete and
     uncorrupted).

---

## Implementation Details (0.5.0)

The fix implements **Option B — Existence check before overwrite**:

1. **`EventFileManager.WriteEventAsync`** — added `bool allowOverwrite = false` parameter.
   When `false` and the destination exists, the write is skipped (idempotent).
   When `true` (used by `AddTagsAsync`), the existing ReadOnly-stripping and overwrite
   logic is preserved.

2. **`LedgerManager.ReconcileLedgerAsync`** — new method that scans the events directory
   for the highest-numbered event file and updates the ledger if it is behind.

3. **`FileSystemEventStore.AppendAsync`** — calls `ReconcileLedgerAsync` on the first
   append after startup, inside the cross-process lock, before position allocation.
   Cached with a `_reconciled` flag so it only runs once per process lifetime.

See the [0.5.0 roadmap](../future-plans/0.5.0-roadmap.md#5-crash-recovery-position-collision)
for the design rationale and the
[implementation tasks](../releases/0.5.0-crash-recovery-tasks.md) for the full task list.
