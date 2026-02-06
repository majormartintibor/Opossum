# The Ledger File

## Overview

The `.ledger` file is a critical component of Opossum's file system-based event store. It acts as the **single source of truth** for tracking sequence positions, ensuring that every event receives a unique, sequential position even under concurrent append operations.

---

## File Location & Format

**Location:** `/Context/{ContextName}/.ledger`

**Format:** JSON

```json
{
  "LastSequencePosition": 1542,
  "EventCount": 1542
}
```

---

## Why Is It Needed?

### The Problem: Concurrent Position Allocation

Without central coordination, concurrent appends create race conditions:

```
Thread A wants to append Event X:
  1. Scan directory: Last event file is "0042.json"
  2. Assign position: 43
  3. Write file: "0043.json"
  
Thread B wants to append Event Y (concurrent):
  1. Scan directory: Last event file is "0042.json" ← SAME!
  2. Assign position: 43 ← DUPLICATE POSITION!
  3. Write file: "0043.json" ← OVERWRITES Thread A's event!

Result: Lost event, corrupted sequence 💥
```

### The Solution: Centralized Ledger

The ledger provides atomic position allocation:

```
Thread A:
  1. Lock ledger
  2. Read: LastSequencePosition = 42
  3. Allocate: Position 43
  4. Write event file "0043.json"
  5. Update ledger: LastSequencePosition = 43
  6. Unlock ledger

Thread B (concurrent):
  1. Lock ledger ← BLOCKS until Thread A releases
  2. Read: LastSequencePosition = 43 ← Updated!
  3. Allocate: Position 44 ← Different, no collision!
  4. Write event file "0044.json"
  5. Update ledger: LastSequencePosition = 44
  6. Unlock ledger

Result: Sequential positions, no duplicates ✅
```

---

## What Is It Used For?

### 1. Sequence Position Allocation (Primary Purpose)

Every event needs a globally unique, sequential position. The ledger provides this atomically:

```csharp
// Get next available position
var nextPosition = await ledgerManager.GetNextSequencePositionAsync(contextPath);
// nextPosition = LastSequencePosition + 1

// Assign to event
event.Position = nextPosition;

// Write event file...

// Update ledger
await ledgerManager.UpdateSequencePositionAsync(contextPath, nextPosition);
```

### 2. Event Count Tracking

The ledger stores the total event count, enabling:
- Quick count queries (without scanning all files)
- Metrics and dashboards
- Capacity planning

### 3. Bootstrap on Restart

When the application restarts, the ledger provides the starting point:

```csharp
// Application restart
var lastPosition = await ledgerManager.GetLastSequencePositionAsync(contextPath);
// lastPosition = 1542

// Next append starts at position 1543
```

**Without the ledger:** You'd need to scan ALL event files to find the highest position (slow for millions of events).

---

## How Does It Work?

### Atomic Write Strategy

The ledger uses a **temp file + atomic rename** pattern to ensure consistency:

```csharp
// 1. Write to temporary file
var tempPath = ledgerPath + $".tmp.{Guid.NewGuid():N}";
await File.WriteAllTextAsync(tempPath, json);

// 2. Atomic rename (replaces old ledger)
File.Move(tempPath, ledgerPath, overwrite: true);

// Result: Either old ledger exists OR new ledger exists, never corrupted
```

**Why this works:**
- `File.Move(..., overwrite: true)` is atomic on most file systems
- Either the old ledger exists (move failed) or new ledger exists (move succeeded)
- Never a partially-written ledger file
- Crash-safe: Process crash during write leaves old ledger intact

### Concurrency Model

#### Append Serialization

Opossum uses a **semaphore** to serialize all appends:

```csharp
private readonly SemaphoreSlim _appendLock = new(1, 1); // Only 1 concurrent append

public async Task AppendAsync(SequencedEvent[] events, AppendCondition? condition)
{
    await _appendLock.WaitAsync(); // Block if another append in progress
    try
    {
        // 1. Validate AppendCondition
        // 2. Allocate positions from ledger
        // 3. Write event files
        // 4. Update indices
        // 5. Update ledger
    }
    finally
    {
        _appendLock.Release();
    }
}
```

**Why serialize appends?**
- ✅ Ensures sequential positions (no gaps)
- ✅ Simplifies AppendCondition validation (no concurrent state changes)
- ✅ Prevents ledger update race conditions
- ⚠️ Trade-off: Appends cannot happen in parallel (acceptable for Opossum's use cases)

#### Retry Logic for Reads

Multiple threads can read the ledger simultaneously, with retry logic for temporary conflicts:

```csharp
public async Task<long> GetLastSequencePositionAsync(string contextPath)
{
    var maxRetries = 5;
    var retryDelay = 10; // milliseconds
    
    for (int attempt = 0; attempt < maxRetries; attempt++)
    {
        try
        {
            using var stream = new FileStream(
                ledgerPath, 
                FileMode.Open, 
                FileAccess.Read, 
                FileShare.Read); // Allow concurrent reads
            
            var ledgerData = await JsonSerializer.DeserializeAsync<LedgerData>(stream);
            return ledgerData?.LastSequencePosition ?? 0;
        }
        catch (IOException) when (attempt < maxRetries - 1)
        {
            // File might be locked by writer, retry with exponential backoff
            await Task.Delay(retryDelay);
            retryDelay *= 2; // 10ms, 20ms, 40ms, 80ms, 160ms
        }
    }
    
    // Final attempt (throw if still fails)
}
```

---

## Ledger Lifecycle

### Initialization (First Event)

```
Initial state: No .ledger file exists

First append:
  ├── GetLastSequencePositionAsync() → Returns 0 (no file)
  ├── GetNextSequencePositionAsync() → Returns 1
  ├── Assign position 1 to event
  ├── Write event file "0001.json"
  └── UpdateSequencePositionAsync(1) → Creates .ledger

Result:
  .ledger: { "LastSequencePosition": 1, "EventCount": 1 }
```

### Normal Operation (Ongoing Appends)

```
Current state: .ledger has LastSequencePosition = 42

Next append (3 events):
  ├── GetNextSequencePositionAsync() → Returns 43
  ├── Assign positions 43, 44, 45 to events
  ├── Write event files "0043.json", "0044.json", "0045.json"
  └── UpdateSequencePositionAsync(45) → Updates .ledger

Result:
  .ledger: { "LastSequencePosition": 45, "EventCount": 45 }
```

### Corruption Recovery

```
Scenario: .ledger file is corrupted (invalid JSON)

Next read:
  ├── GetLastSequencePositionAsync()
  ├── Try to deserialize → JsonException
  ├── Catch exception → Return 0
  └── Ledger will be recreated on next append

Result: Graceful degradation (assumes empty store)
```

**Important:** The ledger is **derived state** - it can be rebuilt from event files if needed (though this is slow for large event stores).

---

## Design Decisions

### Why JSON Instead of Binary?

**Chosen:** JSON format
```json
{
  "LastSequencePosition": 1542,
  "EventCount": 1542
}
```

**Rationale:**
- ✅ Human-readable (easy debugging and inspection)
- ✅ Easy to manually fix if corrupted
- ✅ Extensible (can add fields without breaking)
- ✅ Simple to implement and maintain
- ⚠️ Slightly larger than binary (60 bytes vs 8 bytes)
- ⚠️ Slower to parse (negligible - only 2 integers)

**Trade-off:** Simplicity and debuggability > minimal performance gain

### Why Temp File + Rename?

**Chosen:** Write to `.ledger.tmp.{guid}`, then rename to `.ledger`

**Rationale:**
- ✅ Atomic operation (file move is atomic on most file systems)
- ✅ Never corrupted (either old or new exists, not half-written)
- ✅ Crash-safe (process crash during write leaves old ledger intact)
- ✅ No need for write-ahead log or journaling

**How it guarantees atomicity:**
```
Before:
  .ledger (position: 42)

During write:
  .ledger (position: 42) ← Still exists
  .ledger.tmp.abc123 (position: 43) ← Writing...

Atomic rename:
  .ledger (position: 43) ← Replaced atomically
  .ledger.tmp.abc123 ← Deleted

After:
  .ledger (position: 43)
```

### Why Not a Database?

**Opossum Choice:** File-based ledger

**Rationale:**
- ✅ No external dependencies (critical for factory use case - "no database policy")
- ✅ Simple and transparent (just a JSON file)
- ✅ Cross-platform (works on Windows, Linux, macOS)
- ✅ Backup-friendly (just copy file)
- ✅ No database licensing costs
- ⚠️ No multi-process support (OK for Opossum's single-instance deployments)

---

## Performance Characteristics

### Overhead per Append

```
Append operation breakdown:
├── Validate AppendCondition: ~1-5ms (if specified)
├── Read ledger: ~0.5ms (cached by OS)
├── Allocate position: ~0.01ms (in-memory)
├── Write event file: ~2-5ms (SSD)
├── Update indices: ~1-2ms
└── Update ledger: ~1-2ms (temp file + rename)
    Total: ~5-15ms per append
```

**Ledger overhead:** ~1-2ms (10-20% of total append time)

### Scalability

- **Event count:** Ledger size is constant (~60 bytes regardless of event count)
- **Read performance:** O(1) - single file read
- **Write performance:** O(1) - single file write
- **No degradation:** Performance stays constant from 1 to 1,000,000,000 events

---

## Troubleshooting

### Ledger Corruption

**Symptom:** `GetLastSequencePositionAsync()` returns 0 unexpectedly

**Cause:** Ledger file contains invalid JSON

**Resolution:**
1. Check ledger file content: `cat /path/to/.ledger`
2. If corrupted, delete ledger: `rm /path/to/.ledger`
3. On next append, ledger will be recreated starting at position 0
4. **Warning:** This resets sequence positions - only do this if you know the implications

**Prevention:**
- Use reliable storage (SSD with power-loss protection)
- Ensure adequate disk space
- Don't manually edit ledger files

### Concurrent Access Issues

**Symptom:** `IOException` or `UnauthorizedAccessException` when reading ledger

**Cause:** Another process is writing to the ledger

**Resolution:**
- Retry logic handles this automatically (up to 5 retries with exponential backoff)
- If persistent, ensure only one Opossum instance is accessing the context

### Missing Ledger After Initialization

**Symptom:** No `.ledger` file exists even after appending events

**Cause:** File system permissions issue or disk full

**Resolution:**
1. Check write permissions on context directory
2. Check available disk space
3. Check logs for write errors

---

## Example: Real-World Flow

### Car Dealership: Concurrent Sales

```
Scenario: Two salespeople simultaneously sell cars

Thread A (Salesperson 1 sells to Alice):
─────────────────────────────────────────────────
09:00:00.000 - Lock semaphore
09:00:00.001 - Read ledger: LastPosition = 152
09:00:00.002 - Allocate position 153
09:00:00.005 - Write VehicleSold event to "0153.json"
09:00:00.007 - Update indices
09:00:00.009 - Update ledger: LastPosition = 153
09:00:00.010 - Release semaphore

Thread B (Salesperson 2 sells to Bob):
─────────────────────────────────────────────────
09:00:00.001 - Lock semaphore ← BLOCKS (Thread A has lock)
09:00:00.010 - Acquired lock (Thread A released)
09:00:00.011 - Read ledger: LastPosition = 153 ← Updated!
09:00:00.012 - Allocate position 154
09:00:00.015 - Write VehicleSold event to "0154.json"
09:00:00.017 - Update indices
09:00:00.019 - Update ledger: LastPosition = 154
09:00:00.020 - Release semaphore

Result:
  Alice's sale: Position 153 ✅
  Bob's sale: Position 154 ✅
  No conflicts, sequential order maintained!
```

---

## Related Documentation

- **[Event Store Architecture](./EVENT_STORE.md)** - How events are stored and retrieved
- **[Concurrency Model](./CONCURRENCY.md)** - How Opossum handles concurrent operations
- **[DCB Specification](../Specification/DCB-Specification.md)** - Dynamic Consistency Boundaries

---

## References

- Implementation: `src/Opossum/Storage/FileSystem/LedgerManager.cs`
- Tests: `tests/Opossum.UnitTests/Storage/FileSystem/LedgerManagerTests.cs`
- Usage: `src/Opossum/Storage/FileSystem/FileSystemEventStore.cs`

---

**Last Updated:** 2024  
**Status:** Production Ready  
**Maintained By:** Opossum Project Team
