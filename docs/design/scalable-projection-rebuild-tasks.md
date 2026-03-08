# Scalable Projection Rebuild — Implementation Tasks

> **Architecture document:** docs/design/scalable-projection-rebuild-architecture.md
> **Status tracker:** docs/design/scalable-projection-rebuild-status.md
> **Target version:** 0.5.0-preview.1

---

## Reading This Document

Tasks are grouped into phases. Phases must be completed in order because later phases
depend on earlier ones. Within a phase, tasks that have no dependency on each other are
noted as parallelisable.

**Task ID format:** `P{phase}-T{number}`

**Dependency notation:**
> Depends on: P1-T3, P1-T4

means this task cannot start until P1-T3 and P1-T4 are complete.

---

## Phase 0 — Approval Gate

~~These tasks must be completed before any code is written.~~

### P0-T1 — ~~Approve Breaking API Change~~ ✅ Approved

**Description:**
`IProjectionManager` will lose four methods (`RebuildAsync(string)`,
`RebuildAllAsync(bool)`, `RebuildAsync(string[])`, `GetRebuildStatusAsync`) which are
moved to the new `IProjectionRebuilder` interface.

Additionally, `RebuildAsync(string, CancellationToken)` changes return type from `Task`
to `Task<ProjectionRebuildResult>`, giving callers timing and event-count details.

**Status:** Approved. Opossum is pre-1.0 and not yet used in production. Breaking changes
that improve the library are acceptable.

---

## Phase 1 — Architectural Separation

Extract all rebuild logic from `ProjectionManager` into a dedicated `ProjectionRebuilder`
class. No behavioural changes to the rebuild algorithm yet. The goal of this phase is a
clean separation with all existing tests still passing.

### P1-T1 — Define `IProjectionRebuilder` interface

**File:** `src/Opossum/Projections/IProjectionRebuilder.cs`

**What to create:**
New public interface with the following methods:
```
ResumeInterruptedRebuildsAsync(CancellationToken) → Task
RebuildAsync(string projectionName, CancellationToken) → Task<ProjectionRebuildResult>
RebuildAllAsync(bool forceRebuild, CancellationToken) → Task<ProjectionRebuildResult>
RebuildAsync(string[] projectionNames, CancellationToken) → Task<ProjectionRebuildResult>
GetRebuildStatusAsync() → Task<ProjectionRebuildStatus>
```

All XML documentation must be thorough. `ResumeInterruptedRebuildsAsync` must document
that it is called automatically by `ProjectionDaemon` on startup and is not intended for
direct use by application code.

**Depends on:** P0-T1

---

### P1-T2 — Add internal registration accessor and `BeginRebuildAsync(tempPath)` to `ProjectionManager`

**File:** `src/Opossum/Projections/ProjectionManager.cs`

**What to add:**
1. An `internal` method `GetRegistration(string name) → ProjectionRegistration?`
   that returns the abstract `ProjectionRegistration` base for the named projection
   (from `_projections` dictionary), or `null` if not registered.

2. A new abstract method on `ProjectionRegistration`:
   ```csharp
   public abstract Task BeginRebuildAsync(string tempPath);
   ```
   `ProjectionRegistration<TState>` implements it by calling
   `((FileSystemProjectionStore<TState>)_store).BeginRebuild(tempPath)`.
   This overload is required for `ProjectionRebuilder` to pass an explicit temp path
   through the abstraction layer during crash recovery resume.

This accessor is required by `ProjectionRebuilder` to call `BeginRebuildAsync`,
`ApplyAsync`, and `CommitRebuildAsync` without having to own the registration dictionary.

**What NOT to change:** The registration dictionary itself stays in `ProjectionManager`.
The `ProjectionRegistration` abstract class stays as an inner class of `ProjectionManager`.

**Depends on:** P0-T1

---

### P1-T3 — Create `ProjectionRebuildJournal` model

**File:** `src/Opossum/Projections/ProjectionRebuildJournal.cs`

**What to create:**
`internal sealed class ProjectionRebuildJournal` with properties:
- `ProjectionName: string`
- `TempPath: string`
- `StoreHeadAtStart: long`
- `ResumeFromPosition: long` (mutable — updated on each flush)
- `StartedAt: DateTimeOffset`
- `LastFlushedAt: DateTimeOffset` (mutable)

No file I/O in this class — it is a plain data model used for JSON serialisation.
JSON options: `WriteIndented = true`, `PropertyNameCaseInsensitive = true`.

**Depends on:** nothing (can start in parallel with P1-T1, P1-T2)

---

### P1-T4 — Create `ProjectionRebuilder` class (initial skeleton)

**File:** `src/Opossum/Projections/ProjectionRebuilder.cs`

**What to create:**
`internal sealed partial class ProjectionRebuilder : IProjectionRebuilder`

Constructor parameters:
- `OpossumOptions options`
- `IEventStore eventStore`
- `ProjectionManager projectionManager` (concrete type for internal access)
- `ProjectionOptions projectionOptions`
- `ILogger<ProjectionRebuilder>? logger`

In the constructor, derive `_checkpointPath` using the same logic as `ProjectionManager`
(same path: `{options.RootPath}/{options.StoreName}/Projections/_checkpoints`).

Move the following from `ProjectionManager` into `ProjectionRebuilder`:
- The entire `RebuildProjectionCoreAsync` private method (renamed to `RebuildCoreAsync`
  to distinguish from the public overload).
- The three public rebuild overloads: `RebuildAsync(string)`, `RebuildAllAsync(bool)`,
  `RebuildAsync(string[])`.
- `GetRebuildStatusAsync`.
- All rebuild status tracking: `_currentRebuildStatus`, `_rebuildLock`,
  `MoveToInProgress`, `RemoveFromInProgress`, `UpdateRebuildStatus`.
- All rebuild-related `LoggerMessage` partial methods.

Add `ResumeInterruptedRebuildsAsync` as a stub that returns `Task.CompletedTask`
for now (implemented in Phase 3).

In `RebuildCoreAsync`, replace direct calls to `registration.BeginRebuildAsync()` and
`registration.CommitRebuildAsync()` with calls that go through `projectionManager.GetRegistration(name)`.

**Depends on:** P1-T1, P1-T2, P1-T3

---

### P1-T5 — Remove rebuild code from `ProjectionManager`

**File:** `src/Opossum/Projections/ProjectionManager.cs`

**What to remove:**
- `RebuildProjectionCoreAsync` (moved to `ProjectionRebuilder`).
- `RebuildAsync(string, CancellationToken)`.
- `RebuildAllAsync(bool, CancellationToken)`.
- `RebuildAsync(string[], CancellationToken)`.
- `GetRebuildStatusAsync`.
- `MoveToInProgress`, `RemoveFromInProgress`, `UpdateRebuildStatus`.
- `_currentRebuildStatus`, `_rebuildLock`.
- All rebuild-related `LoggerMessage` partial methods.

**What to keep:**
Everything related to registration, `UpdateAsync`, checkpoints, and `GetRegisteredProjections`.

**Depends on:** P1-T4 (rebuilder must exist before removing from manager)

---

### P1-T6 — Remove rebuild methods from `IProjectionManager`

**File:** `src/Opossum/Projections/IProjectionManager.cs`

**What to remove:**
- `RebuildAsync(string projectionName, CancellationToken)`.
- `RebuildAllAsync(bool forceRebuild, CancellationToken)`.
- `RebuildAsync(string[] projectionNames, CancellationToken)`.
- `GetRebuildStatusAsync()`.

**Depends on:** P1-T5

---

### P1-T7 — Register `ProjectionRebuilder` in DI

**File:** `src/Opossum/Projections/ProjectionServiceCollectionExtensions.cs`

**What to change:**
Register `IProjectionRebuilder` → `ProjectionRebuilder` as a singleton, after the
`IProjectionManager` registration.

Note: `ProjectionRebuilder` requires a `ProjectionManager` (concrete type) not the
interface, because it needs the internal `GetRegistration` method. Use a factory
registration:
```csharp
services.AddSingleton<IProjectionRebuilder>(sp =>
    new ProjectionRebuilder(
        sp.GetRequiredService<OpossumOptions>(),
        sp.GetRequiredService<IEventStore>(),
        (ProjectionManager)sp.GetRequiredService<IProjectionManager>(),
        sp.GetRequiredService<ProjectionOptions>(),
        sp.GetService<ILogger<ProjectionRebuilder>>()));
```

**Depends on:** P1-T4, P1-T5

---

### P1-T8 — Update `ProjectionDaemon` to use `IProjectionRebuilder`

**File:** `src/Opossum/Projections/ProjectionDaemon.cs`

**What to change:**
- Add constructor parameter `IProjectionRebuilder projectionRebuilder`.
- In `RebuildMissingProjectionsAsync`, call `_projectionRebuilder.RebuildAllAsync(...)`.
- Add a call to `_projectionRebuilder.ResumeInterruptedRebuildsAsync(stoppingToken)`
  before the existing `RebuildMissingProjectionsAsync` call (order matters).

**What NOT to change:** The polling loop. `UpdateAsync` is still called on `IProjectionManager`.

**Depends on:** P1-T1, P1-T7

---

### P1-T9 — Update sample application admin endpoints

**Files:** Admin endpoint handlers in `Opossum.Samples.CourseManagement` that currently
call `IProjectionManager.RebuildAsync` / `RebuildAllAsync`.

**What to change:**
Inject `IProjectionRebuilder` and use it for rebuild triggers. Remove the inject of
`IProjectionManager` from any endpoint that only used it for rebuild.

**Depends on:** P1-T1, P1-T7

---

### P1-T10 — Update integration tests that call rebuild via `IProjectionManager`

**Files:** `tests/Opossum.IntegrationTests/Projections/ProjectionRebuildTests.cs`,
`tests/Opossum.IntegrationTests/Projections/ParallelRebuildTests.cs`,
`tests/Opossum.IntegrationTests/Projections/ParallelRebuildLockingTests.cs`
and any other test that calls rebuild methods through `IProjectionManager`.

**What to change:**
Resolve `IProjectionRebuilder` from the service provider and call rebuild methods on it.
The test setup already builds a `ServiceProvider` so `GetRequiredService<IProjectionRebuilder>()`
works directly.

**Depends on:** P1-T7, P1-T8

---

### P1-T11 — Verify Phase 1: build + full test suite

**What to do:**
1. `dotnet build` — verify 0 errors, 0 warnings.
2. `dotnet test tests/Opossum.UnitTests/`
3. `dotnet test tests/Opossum.IntegrationTests/`
4. `dotnet test tests/Samples/Opossum.Samples.CourseManagement.IntegrationTests/`
5. Verify all tests that existed before Phase 1 still pass.

**Acceptance criterion:** All tests green; 0 build warnings.

**Depends on:** P1-T1 through P1-T10

---

## Phase 2 — Write-Through Store

Replace the in-memory `_rebuildStateBuffer` with direct writes to the temp directory
during event replay. This is the primary memory fix.

### P2-T1 — Add `BeginRebuild(string tempPath)` overload to `FileSystemProjectionStore`

**File:** `src/Opossum/Projections/FileSystemProjectionStore.cs`

**What to change:**
The existing `BeginRebuild()` (no arg) generates a new GUID-based temp path. Add a new
internal overload `BeginRebuild(string tempPath)` that accepts an explicit path (used for
resume in Phase 3). Both overloads:
- Set `_isInRebuild = true` (rename `_rebuildMode` to `_isInRebuild`).
- Set `_rebuildTempPath = tempPath`.
- Call `Directory.CreateDirectory(_rebuildTempPath)` if it does not already exist
  (idempotent — safe for both fresh and resume cases).
- Initialise `_tagAccumulator = new Dictionary<string, HashSet<string>>()`.
- Clear `_projectionTags`.

**Depends on:** P1-T11

---

### P2-T2 — Remove `_rebuildStateBuffer`; implement write-through `SaveAsync`

**File:** `src/Opossum/Projections/FileSystemProjectionStore.cs`

**What to change:**

Remove field:
```csharp
private readonly Dictionary<string, TState?> _rebuildStateBuffer = [];
```

Change `SaveAsync` rebuild-mode branch from:
```csharp
if (_rebuildMode)
{
    _rebuildStateBuffer[key] = state;
    return;
}
```
to a direct file write that:
1. Wraps `state` in `ProjectionWithMetadata<TState>` with new metadata
   (`CreatedAt = now`, `LastUpdatedAt = now`, `Version = 1`).
2. Serialises to JSON.
3. Calls `File.WriteAllTextAsync(GetFilePath(key), json, cancellationToken)`.
   (`GetFilePath` already resolves to `_rebuildTempPath` when it is set.)
4. Applies write-protect attribute if `_writeProtect` is true.
5. If `_tagProvider != null`, computes tags for `state` and adds each tag to
   `_tagAccumulator[tagIndexKey].Add(key)`.
   The tag index key is the same sanitised string used by `ProjectionTagIndex.GetIndexFilePath`.

Note: Step 5 replaces the role of `_projectionTags` during rebuild. `_projectionTags` is
only needed for the live-update path to track old tags for delta updates.

**Depends on:** P2-T1

---

### P2-T3 — Update `GetAsync` rebuild-mode branch

**File:** `src/Opossum/Projections/FileSystemProjectionStore.cs`

**What to change:**
Change the rebuild-mode `GetAsync` branch from:
```csharp
if (_rebuildMode)
{
    return _rebuildStateBuffer.TryGetValue(key, out var buffered) ? buffered : null;
}
```
to read the file from the temp directory:
```csharp
if (_isInRebuild)
{
    var tempFilePath = GetFilePath(key);  // resolves to _rebuildTempPath
    if (!File.Exists(tempFilePath)) return null;
    var json = await File.ReadAllTextAsync(tempFilePath, cancellationToken).ConfigureAwait(false);
    var wrapper = JsonSerializer.Deserialize<ProjectionWithMetadata<TState>>(json, _jsonOptions);
    return wrapper?.Data;
}
```

**Important:** Do NOT fall back to the production directory. The temp directory is the
authoritative state during rebuild. If the key has not been replayed yet, `null` is correct.

**Depends on:** P2-T2

---

### P2-T4 — Update `DeleteAsync` rebuild-mode branch

**File:** `src/Opossum/Projections/FileSystemProjectionStore.cs`

**What to change:**
Change the rebuild-mode `DeleteAsync` branch from:
```csharp
if (_rebuildMode)
{
    _rebuildStateBuffer.Remove(key);
    return;
}
```
to delete the file from the temp directory (if it exists) and remove the key from
`_tagAccumulator`:
```csharp
if (_isInRebuild)
{
    var tempFilePath = GetFilePath(key);
    if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
    // remove from tag accumulator
    foreach (var keySet in _tagAccumulator!.Values)
        keySet.Remove(key);
    return;
}
```

**Depends on:** P2-T2

---

### P2-T5 — Rewrite `CommitRebuildAsync`

**File:** `src/Opossum/Projections/FileSystemProjectionStore.cs`

**What to change:**
Remove the entire `foreach` loop that iterates `_rebuildStateBuffer` and writes files.
Remove the `metadataEntries` dictionary.
Remove the call to `_metadataIndex.BatchSaveAsync`.
Remove the call to `_metadataIndex.ClearAsync` from the commit path.

The new `CommitRebuildAsync` does only:
1. Write tag index files from `_tagAccumulator` to `_rebuildTempPath/Indices/` in parallel.
   - For each tag key in `_tagAccumulator`:
     - Ensure `Indices/` subdirectory under `_rebuildTempPath` exists.
     - Serialise `HashSet<string>` to JSON.
     - Write atomically (temp file + rename) to the tag index file path inside `_rebuildTempPath`.
2. Atomic swap: `DeleteDirectory(_projectionPath)` → `Directory.Move(_rebuildTempPath, _projectionPath)`.
3. `finally`: reset `_isInRebuild = false`, `_rebuildTempPath = null`, `_tagAccumulator = null`.

The parallel tag write uses `Parallel.ForEachAsync` with
`MaxDegreeOfParallelism = Environment.ProcessorCount`.

Error handling: if anything in step 1 or 2 throws, clean up the temp directory and rethrow
(same pattern as current code).

**Depends on:** P2-T2, P2-T3, P2-T4

---

### P2-T6 — Remove dead code

**File:** `src/Opossum/Projections/FileSystemProjectionStore.cs`

Remove the following methods that are now dead (were only called from `ClearAsync` on
`ProjectionRegistration`, which itself is only reachable from unused code):
- `ClearProjectionFiles()`
- `DeleteAllIndicesAsync()`

**File:** `src/Opossum/Projections/ProjectionManager.cs` (inner class `ProjectionRegistration<TState>`)

Remove `ClearAsync` override from `ProjectionRegistration<TState>`.
Remove `ClearAsync` abstract declaration from `ProjectionRegistration` base class.

**Depends on:** P2-T5

---

### P2-T7 — Verify Phase 2: build + full test suite

**What to do:**
1. `dotnet build` — 0 errors, 0 warnings.
2. Run full test suite as in P1-T11.
3. Manually verify: run sample application, trigger a rebuild, observe that the
   production projection directory is NOT touched during replay and that the temp
   directory grows incrementally.

**Acceptance criterion:** All tests green; 0 build warnings; observed write-through
behaviour in temp directory during rebuild.

**Depends on:** P2-T1 through P2-T6

---

## Phase 3 — Rebuild Journal and Crash Recovery

Add durable rebuild progress tracking and the ability to resume interrupted rebuilds.

### P3-T1 — Add `RebuildFlushInterval` to `ProjectionOptions`

**File:** `src/Opossum/Projections/ProjectionOptions.cs`

**What to add:**
```csharp
[Range(100, 1_000_000)]
public int RebuildFlushInterval { get; set; } = 10_000;
```

Add full XML documentation matching the guidance in the architecture document.
Add validation in `ProjectionOptionsValidator` for the valid range.

**Depends on:** P2-T7

---

### P3-T2 — Implement journal and tag accumulator file I/O in `ProjectionRebuilder`

**File:** `src/Opossum/Projections/ProjectionRebuilder.cs`

**What to add (private methods):**

`GetJournalFilePath(string projectionName) → string`
Returns `{_checkpointPath}/{projectionName}.rebuild.json`.

`GetTagAccumulatorFilePath(string projectionName) → string`
Returns `{_checkpointPath}/{projectionName}.rebuild.tags.json`.

`CreateJournalAsync(string name, string tempPath, long storeHead, CancellationToken) → Task`
Creates and atomically writes a new `ProjectionRebuildJournal` with
`ResumeFromPosition = 0`, `StartedAt = now`.

`FlushJournalAsync(string name, long position, CancellationToken) → Task`
Reads the existing journal, sets `ResumeFromPosition = position`,
`LastFlushedAt = now`, writes back atomically (temp file + rename).

`FlushTagAccumulatorAsync(string name, Dictionary<string, HashSet<string>> tagAccumulator, CancellationToken) → Task`
Serialises the tag accumulator as a companion file `{name}.rebuild.tags.json`.
Written atomically (temp file + rename) alongside the journal flush.

`LoadTagAccumulatorAsync(string name, CancellationToken) → Task<Dictionary<string, HashSet<string>>?>`
Returns the deserialised tag accumulator or `null` if the companion file does not exist.
Used during crash recovery to restore pre-crash tag state.

`ReadJournalAsync(string name, CancellationToken) → Task<ProjectionRebuildJournal?>`
Returns the deserialised journal or `null` if the file does not exist.

`DeleteJournalAsync(string name, CancellationToken) → Task`
Deletes `{projectionName}.rebuild.json` and `{projectionName}.rebuild.tags.json`
if they exist.

Use `WriteIndented = true`, `PropertyNameCaseInsensitive = true` JSON options.
All writes must use the atomic temp-file + rename pattern.

**Depends on:** P1-T4, P2-T7

---

### P3-T3 — Integrate journal and tag accumulator persistence into `RebuildCoreAsync`

**File:** `src/Opossum/Projections/ProjectionRebuilder.cs`

**What to change in `RebuildCoreAsync`:**

**At the start (after creating temp directory):**
```
await CreateJournalAsync(name, tempPath, storeHead, ct)
```

**Inside the event batch loop, after updating `totalEventsProcessed`:**
```csharp
if (totalEventsProcessed % _projectionOptions.RebuildFlushInterval == 0)
{
    await FlushJournalAsync(name, lastCheckpointPosition, ct);
    await FlushTagAccumulatorAsync(name, tagAccumulator, ct);
}
```

Both files are flushed together so that on resume the journal position and tag
accumulator are consistent.

**At the end (after successful commit and `SaveCheckpointAsync`):**
```
await DeleteJournalAsync(name, ct)  // also deletes tags companion file
```

**On error (in `catch` block of `RebuildAsync(string[])`):**
Journal and tags file are NOT deleted on error. They persist so that
`ResumeInterruptedRebuildsAsync` can find and resume on next startup.

**Depends on:** P3-T2

---

### P3-T4 — Implement `ResumeInterruptedRebuildsAsync`

**File:** `src/Opossum/Projections/ProjectionRebuilder.cs`

**What to implement:**

```
ResumeInterruptedRebuildsAsync(CancellationToken ct):

1. Enumerate all *.rebuild.json files in _checkpointPath
2. For each journal file:
   a. ReadJournalAsync(name)
   b. IF journal.TempPath does NOT exist as a directory:
      - Log warning: "Rebuild journal for '{name}' references missing temp dir at '{tempPath}'. Journal discarded; projection will be rebuilt from scratch."
      - DeleteJournalAsync(name)  [also deletes tags companion file]
      - Skip (the normal RebuildAllAsync call will pick it up as missing-checkpoint)
   c. IF journal.TempPath EXISTS:
      - Log information: "Resuming interrupted rebuild of '{name}' from event position {resumeFromPosition} (store head was {storeHeadAtStart}, started {startedAt:u})"
      - Get registration via _projectionManager.GetRegistration(name)
      - IF registration is null: log error, delete journal+tags and temp dir, skip
      - Call registration.BeginRebuildAsync(journal.TempPath)  [reuses existing temp dir]
      - LoadTagAccumulatorAsync(name) → restore _tagAccumulator into the store
        (tags for events ≤ resumeFromPosition are already in the persisted file)
      - Call RebuildCoreAsync(name, fromPosition=journal.ResumeFromPosition,
                              tempPath=journal.TempPath,
                              storeHead=journal.StoreHeadAtStart, ct)
3. CleanOrphanedTempDirectoriesAsync(ct)
```

**Critical:** The tag accumulator MUST be loaded from the companion file before the replay
loop starts. Without it, tag index files at commit would only contain keys from the resumed
portion, missing all keys processed before the crash.

**Depends on:** P3-T3

---

### P3-T5 — Implement `CleanOrphanedTempDirectoriesAsync`

**File:** `src/Opossum/Projections/ProjectionRebuilder.cs`

**What to implement:**
Scan `{RootPath}/{StoreName}/Projections/` for directories matching `*.tmp.*` pattern
(any projection name followed by `.tmp.` and a GUID). For each such directory, check
whether a corresponding `*.rebuild.json` exists in `_checkpointPath`. If no journal
exists, delete the orphaned temp directory and log an informational message.

**Depends on:** P3-T4

---

### P3-T6 — Update `ProjectionDaemon` to call `ResumeInterruptedRebuildsAsync`

**File:** `src/Opossum/Projections/ProjectionDaemon.cs`

**What to change:**
Before the existing `RebuildMissingProjectionsAsync` call (which calls
`_projectionRebuilder.RebuildAllAsync`), add:
```csharp
await _projectionRebuilder.ResumeInterruptedRebuildsAsync(stoppingToken).ConfigureAwait(false);
```

This ensures interrupted rebuilds are resumed before the daemon checks for missing
checkpoints (which would trigger a fresh rebuild of the same projection, overwriting
the resume).

**Depends on:** P3-T4, P1-T8

---

### P3-T7 — Write crash recovery integration tests

**File:** `tests/Opossum.IntegrationTests/Projections/ProjectionRebuildCrashRecoveryTests.cs`
(new file)

**Tests to implement:**

1. **`ResumeInterruptedRebuild_WhenJournalExists_ResumesFromFlushPoint`**
   - Seed N events.
   - Manually write a `ProjectionRebuildJournal` with `ResumeFromPosition = M` where
     `M < N`.
   - Create a partial temp directory with pre-built state for positions ≤ M.
   - Write a companion `{name}.rebuild.tags.json` with the tags for those positions.
   - Call `ResumeInterruptedRebuildsAsync`.
   - Assert: final state equals a full rebuild from scratch; journal file deleted;
     tags companion file deleted; checkpoint saved.
   - Assert: tag index files contain keys from ALL events (not just the resumed portion).

2. **`ResumeInterruptedRebuild_WhenTempDirMissing_DeletesJournalAndRebuildsFromScratch`**
   - Write a journal file referencing a temp path that does not exist on disk.
   - Call `ResumeInterruptedRebuildsAsync`.
   - Assert: journal file is deleted; tags companion file is deleted;
     projection is subsequently rebuilt fresh by `RebuildAllAsync`.

3. **`CleanOrphanedTempDir_WhenNoJournalExists_DeletesOrphanedDir`**
   - Create an orphaned temp directory with no matching journal.
   - Call `ResumeInterruptedRebuildsAsync`.
   - Assert: orphaned directory is deleted.

4. **`RebuildJournal_IsFlushedEveryFlushInterval`**
   - Seed 2 × `RebuildFlushInterval` events.
   - Intercept (or verify via file system) that the journal file exists and has a
     non-zero `ResumeFromPosition` after the first flush interval.
   - Also verify the tags companion file exists alongside the journal.

5. **`RebuildJournal_IsDeletedOnSuccessfulCompletion`**
   - Run a complete rebuild.
   - Assert: no `*.rebuild.json` file exists in `_checkpointPath` after completion.
   - Assert: no `*.rebuild.tags.json` file exists in `_checkpointPath` after completion.

6. **`ResumeInterruptedRebuild_TagAccumulatorRestoredCorrectly`**
   - Seed N events where each projection has tags.
   - Manually write journal + tags companion file for position M < N.
   - Create partial temp directory.
   - Resume and complete the rebuild.
   - Assert: tag index files match what a full from-scratch rebuild would produce.

**Depends on:** P3-T6

---

### P3-T8 — Verify Phase 3: build + full test suite

Same procedure as P1-T11 and P2-T7. All existing tests must still pass; all new
crash recovery tests must pass.

**Depends on:** P3-T1 through P3-T7

---

## Phase 4 — Metadata Index Decoupling

Remove the aggregated metadata index from the rebuild critical path.

### P4-T1 — Remove metadata index operations from `CommitRebuildAsync`

**File:** `src/Opossum/Projections/FileSystemProjectionStore.cs`

This was already done as part of P2-T5 (the `BatchSaveAsync` call was removed there).
Verify that `CommitRebuildAsync` makes no calls to `_metadataIndex`. If any remain,
remove them now.

Also verify that `BeginRebuild` does not call `_metadataIndex.ClearAsync`. If it does,
remove that call. The old `index.json` in the production directory is deleted as a
natural consequence of `DeleteDirectory(_projectionPath)` during the atomic swap.

**Depends on:** P3-T8

---

### P4-T2 — Verify lazy metadata index rebuild after projection rebuild

**What to verify (no code change expected):**
After a full rebuild, the `Metadata/index.json` file does NOT exist in the new
production projection directory. Verify that the first call to
`ProjectionMetadataIndex.GetAsync` or `GetAllAsync` on the rebuilt projection
triggers `LoadIndexAsync`, which gracefully handles the missing file (returns null /
empty) and does NOT crash.

If `LoadIndexAsync` has any issue with a missing `Metadata/` directory, add a guard.

**Depends on:** P4-T1

---

### P4-T3 — Add integration test for post-rebuild metadata behaviour

**File:** `tests/Opossum.IntegrationTests/Projections/ProjectionRebuildTests.cs`

**Test to add:**

`RebuildProjection_MetadataIndexNotPresent_StillServesReadsCorrectly`
- Rebuild a projection.
- Verify no `Metadata/index.json` exists.
- Call `IProjectionStore.GetAsync` for a known key.
- Assert: returns correct state (reads from individual file, not aggregated index).

**Depends on:** P4-T2

---

### P4-T4 — Verify Phase 4: build + full test suite

**Depends on:** P4-T1 through P4-T3

---

## Phase 5 — Sample Application and Configuration Guide

### P5-T1 — Update sample application configuration

**File:** `Samples/Opossum.Samples.CourseManagement/appsettings.json` (and `.Development.json`)

**What to add:**
Add `RebuildFlushInterval` to the `Projections` section with an appropriate comment
explaining the crash recovery trade-off.

**Depends on:** P3-T1

---

### P5-T2 — Update configuration guide documentation

**File:** `docs/configuration-guide.md`

**What to add:**
Document `RebuildFlushInterval` with the same guidance as the XML documentation in
`ProjectionOptions`: recommended ranges, trade-offs, relationship to crash recovery.

**Depends on:** P5-T1

---

## Phase 6 — Final Verification and Documentation

### P6-T1 — Update `CHANGELOG.md`

**File:** `CHANGELOG.md`

Under `## [Unreleased]`:

**Added:**
- `IProjectionRebuilder` interface: all rebuild operations now separated from
  `IProjectionManager`.
- `ProjectionRebuilder`: production-grade rebuild engine with write-through mode,
  rebuild journal, and crash recovery.
- `RebuildFlushInterval` on `ProjectionOptions`: configures how often the rebuild
  journal is flushed to disk (default 10,000 events). On restart after a crash,
  at most `RebuildFlushInterval` events need re-processing — not the entire event log.
- Crash recovery for interrupted projection rebuilds: orphaned rebuild journals are
  automatically detected on startup and the rebuild is resumed from the last flush point.
  Tag accumulator state is persisted alongside the journal so that tag indices are correct
  after a resumed rebuild.
- Orphaned temp directory cleanup on startup.

**Changed:**
- Projection rebuild now uses write-through mode: state is written immediately to a
  temp directory during event replay instead of being buffered in memory. Peak memory
  during rebuild is now O(batch_size × state_size) instead of
  O(unique_keys × state_size).
- Tag indices are now built from an in-memory accumulator and written in parallel at
  commit time, replacing per-key sequential read-modify-write cycles.
- The aggregated metadata index (`Metadata/index.json`) is no longer written during
  projection rebuild. It is populated lazily on first query.

**Breaking:**
- `IProjectionManager` no longer exposes rebuild methods. Consumers that call
  `RebuildAsync`, `RebuildAllAsync`, or `GetRebuildStatusAsync` on `IProjectionManager`
  must now inject and use `IProjectionRebuilder`.
- `RebuildAsync(string, CancellationToken)` return type changed from `Task` to
  `Task<ProjectionRebuildResult>`, providing timing and event-count details to callers.

**Depends on:** P4-T4, P5-T2

---

### P6-T2 — Final full test suite run

Run the complete test suite across all projects. Verify:
- 0 build errors.
- 0 build warnings.
- All unit tests pass.
- All integration tests pass (Opossum core).
- All integration tests pass (sample application).

**Depends on:** P6-T1

---

## Summary Table

| ID | Description | Phase | Blocking |
|----|-------------|-------|---------|
| P0-T1 | ~~Approve breaking API change~~ ✅ Approved | 0 | All phases |
| P1-T1 | Define `IProjectionRebuilder` interface | 1 | P1-T4, P1-T7 |
| P1-T2 | Add internal registration accessor + `BeginRebuildAsync(tempPath)` to `ProjectionManager` | 1 | P1-T4 |
| P1-T3 | Create `ProjectionRebuildJournal` model | 1 | P1-T4 |
| P1-T4 | Create `ProjectionRebuilder` skeleton (move rebuild code) | 1 | P1-T5 |
| P1-T5 | Remove rebuild code from `ProjectionManager` | 1 | P1-T6 |
| P1-T6 | Remove rebuild methods from `IProjectionManager` | 1 | P1-T8 |
| P1-T7 | Register `ProjectionRebuilder` in DI | 1 | P1-T8 |
| P1-T8 | Update `ProjectionDaemon` | 1 | P1-T9 |
| P1-T9 | Update sample app admin endpoints | 1 | P1-T11 |
| P1-T10 | Update integration tests | 1 | P1-T11 |
| P1-T11 | Verify Phase 1 | 1 | Phase 2 |
| P2-T1 | Add `BeginRebuild(tempPath)` overload | 2 | P2-T2 |
| P2-T2 | Remove buffer; write-through `SaveAsync` | 2 | P2-T3 |
| P2-T3 | Update `GetAsync` rebuild branch | 2 | P2-T5 |
| P2-T4 | Update `DeleteAsync` rebuild branch | 2 | P2-T5 |
| P2-T5 | Rewrite `CommitRebuildAsync` | 2 | P2-T6 |
| P2-T6 | Remove dead code | 2 | P2-T7 |
| P2-T7 | Verify Phase 2 | 2 | Phase 3 |
| P3-T1 | Add `RebuildFlushInterval` to `ProjectionOptions` | 3 | P3-T2 |
| P3-T2 | Implement journal + tag accumulator file I/O | 3 | P3-T3 |
| P3-T3 | Integrate journal + tag accumulator into `RebuildCoreAsync` | 3 | P3-T4 |
| P3-T4 | Implement `ResumeInterruptedRebuildsAsync` | 3 | P3-T6 |
| P3-T5 | Implement orphaned temp dir cleanup | 3 | P3-T6 |
| P3-T6 | Update `ProjectionDaemon` for resume call | 3 | P3-T7 |
| P3-T7 | Write crash recovery integration tests (6 test cases) | 3 | P3-T8 |
| P3-T8 | Verify Phase 3 | 3 | Phase 4 |
| P4-T1 | Remove metadata index from rebuild path (verify) | 4 | P4-T2 |
| P4-T2 | Verify lazy metadata post-rebuild | 4 | P4-T3 |
| P4-T3 | Integration test: metadata after rebuild | 4 | P4-T4 |
| P4-T4 | Verify Phase 4 | 4 | Phase 5 |
| P5-T1 | Update sample app configuration | 5 | P5-T2 |
| P5-T2 | Update configuration guide | 5 | P6-T1 |
| P6-T1 | Update `CHANGELOG.md` | 6 | P6-T2 |
| P6-T2 | Final full test suite run | 6 | Done |

---

*End of tasks document.*
