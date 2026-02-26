# Known Limitation: Crash-Recovery Position Collision

> **Severity:** High — silent data loss possible
> **Introduced:** v0.1.0
> **Affects:** All versions up to and including 0.4.x
> **Fix target:** 0.5.0 — see [roadmap](../future-plans/0.5.0-roadmap.md#5-crash-recovery-position-collision)

---

## What Happens

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

## Planned Fix (0.5.0)

Two approaches are under consideration; both make the per-event write **idempotent**
on restart:

### Option B — Existence check before overwrite (preferred)

Before calling `File.Move` in `WriteEventAsync`, check `File.Exists(filePath)`. If
the destination already exists, skip the write (the previous run persisted the event).
After writing all event files, reconcile the ledger to
`max(lastSuccessfullyWrittenPosition, currentLedgerPosition)`.

This requires no file-format change and is the lowest-risk fix.

### Option A — Ledger-first (full WAL semantics)

Move the ledger update to **before** step 7. The ledger records intent before event
files are written. On restart, any position in the ledger that lacks a corresponding
event file on disk is detected and the write is re-executed.

More principled but more complex. A startup recovery scan adds latency and the recovery
logic must be carefully tested. Deferred as a future consideration unless Option B proves
insufficient.

The implementation decision will be recorded in a new ADR.

See the [0.5.0 roadmap](../future-plans/0.5.0-roadmap.md#5-crash-recovery-position-collision)
for the full implementation plan.
