# Batched Flush - Future Redesign Plan

## Date: 2025-01-28
## Status: Planning Document (Not Implemented)
## Purpose: How to properly implement batched fsyncs IF we decide to try again

---

## ⚠️ Prerequisites Before Attempting

**DO NOT implement this unless:**

1. ✅ **Proven need:** Benchmarks show >200 events/sec requirement
2. ✅ **Architecture change accepted:** Willing to move away from file-per-event
3. ✅ **Complexity justified:** Performance gain > maintenance burden
4. ✅ **Resources available:** 2-3 weeks of dedicated development time
5. ✅ **Testing capacity:** Can thoroughly test concurrent scenarios

**Current performance (94-130 events/sec) is sufficient for most use cases.**

---

## 🎯 Goals

### What Success Looks Like

**Performance Targets:**
- Throughput: 94 → 250+ events/sec (2.5x improvement)
- Latency: <15ms per event (acceptable)
- Scaling: Linear with concurrent load
- No lock contention under high concurrency

**Quality Targets:**
- No data loss on power failure
- DCB validation still works
- Backward compatible with existing event stores
- Production-ready reliability

---

## 🏗️ Architecture Options

### Option 1: WAL-Style Event Log ✅ (RECOMMENDED)

**Concept:** Write events to append-only log file, batch fsync periodically.

**How It Works:**
```
Event Stream:
Event 1 → Append to WAL (no fsync)
Event 2 → Append to WAL (no fsync)
Event 3 → Append to WAL (no fsync)
Event 4 → Append to WAL (no fsync)
Event 5 → Append to WAL → FSYNC (one syscall for all 5!)
```

**Implementation:**
```csharp
public class WALEventStore
{
    private readonly FileStream _walFile;
    private readonly SemaphoreSlim _writeLock = new(1);
    private readonly List<PendingWrite> _pendingWrites = [];
    private long _currentPosition = 0;

    public async Task AppendAsync(SequencedEvent evt)
    {
        var completion = new TaskCompletionSource();
        
        await _writeLock.WaitAsync();
        try
        {
            // Serialize event
            var bytes = Serialize(evt);
            
            // Write to WAL (no fsync yet)
            await _walFile.WriteAsync(bytes);
            _currentPosition += bytes.Length;
            
            // Track pending write
            _pendingWrites.Add(new PendingWrite(evt.Position, completion));
            
            // If batch is full, flush
            if (_pendingWrites.Count >= 5)
            {
                await FlushWAL(); // Single fsync for all pending!
            }
        }
        finally
        {
            _writeLock.Release();
        }
        
        // Wait for flush
        await completion.Task;
    }
    
    private async Task FlushWAL()
    {
        await _walFile.FlushAsync(); // ← ONE fsync for multiple events!
        
        // Signal all pending writes
        foreach (var pending in _pendingWrites)
        {
            pending.Completion.SetResult();
        }
        _pendingWrites.Clear();
    }
}
```

**Pros:**
- ✅ True batching (one fsync for many events)
- ✅ Sequential writes (fast)
- ✅ Simple synchronization (append-only)
- ✅ Matches database best practices

**Cons:**
- ❌ Requires WAL compaction/archival
- ❌ More complex recovery logic
- ❌ Can't just "ls" to see events
- ❌ Major architecture change

**Estimated Effort:** 2-3 weeks

**Performance Gain:** 2-3x (expected)

---

### Option 2: Lock-Free Concurrent Queue 🔧

**Concept:** Use lock-free data structures to avoid serialization.

**How It Works:**
```csharp
public class LockFreeBatchedFlush
{
    private readonly ConcurrentQueue<PendingFlush> _queue = new();
    private readonly SemaphoreSlim _flushSemaphore = new(1);
    private volatile int _queuedCount = 0;
    
    public async Task QueueFlushAsync(string filePath)
    {
        var completion = new TaskCompletionSource();
        _queue.Enqueue(new PendingFlush(filePath, completion));
        
        var count = Interlocked.Increment(ref _queuedCount);
        
        // First event in batch triggers flush
        if (count == 1)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(_maxDelay);
                await FlushBatchAsync();
            });
        }
        
        // Batch full, try to flush immediately
        if (count >= _batchSize && _flushSemaphore.Wait(0))
        {
            try
            {
                await FlushBatchAsync();
            }
            finally
            {
                _flushSemaphore.Release();
            }
        }
        
        await completion.Task;
    }
    
    private async Task FlushBatchAsync()
    {
        var toFlush = new List<PendingFlush>();
        while (_queue.TryDequeue(out var item))
        {
            toFlush.Add(item);
        }
        Interlocked.Exchange(ref _queuedCount, 0);
        
        // Flush all files in parallel
        await Parallel.ForEachAsync(
            toFlush.Select(f => f.FilePath).Distinct(),
            async (file, ct) => await FlushFile(file));
        
        // Signal completions
        foreach (var item in toFlush)
        {
            item.Completion.SetResult();
        }
    }
}
```

**Pros:**
- ✅ No lock contention in hot path
- ✅ Works with file-per-event architecture
- ✅ Smaller change (no WAL needed)

**Cons:**
- ❌ Still needs to fsync each file separately
- ❌ Complex concurrency logic
- ❌ Hard to get right (race conditions)
- ❌ Limited benefit (still N fsyncs for N files)

**Estimated Effort:** 1-2 weeks

**Performance Gain:** 1.2-1.5x (modest)

---

### Option 3: Background Flush Thread 🔄

**Concept:** Dedicated thread batches and flushes files.

**How It Works:**
```csharp
public class BackgroundFlushCoordinator
{
    private readonly Channel<PendingFlush> _channel;
    private readonly Task _flushTask;
    
    public BackgroundFlushCoordinator()
    {
        _channel = Channel.CreateUnbounded<PendingFlush>();
        _flushTask = Task.Run(FlushWorker);
    }
    
    public async Task QueueFlushAsync(string filePath)
    {
        var completion = new TaskCompletionSource();
        await _channel.Writer.WriteAsync(new PendingFlush(filePath, completion));
        await completion.Task;
    }
    
    private async Task FlushWorker()
    {
        var batch = new List<PendingFlush>();
        
        while (await _channel.Reader.WaitToReadAsync())
        {
            batch.Clear();
            
            // Collect batch (up to 5 items or 2ms timeout)
            var deadline = DateTime.UtcNow.AddMilliseconds(2);
            while (batch.Count < 5 && DateTime.UtcNow < deadline)
            {
                if (_channel.Reader.TryRead(out var item))
                {
                    batch.Add(item);
                }
                else
                {
                    await Task.Delay(1);
                }
            }
            
            // Flush batch
            await FlushFiles(batch.Select(b => b.FilePath).Distinct());
            
            // Signal completions
            foreach (var item in batch)
            {
                item.Completion.SetResult();
            }
        }
    }
}
```

**Pros:**
- ✅ No lock contention (channel is lock-free)
- ✅ Dedicated thread for flushing
- ✅ Works with file-per-event

**Cons:**
- ❌ Still needs to fsync each file
- ❌ Thread overhead
- ❌ Complex shutdown logic
- ❌ Limited benefit (still N fsyncs)

**Estimated Effort:** 1 week

**Performance Gain:** 1.3-1.6x (modest)

---

## 🎯 Recommended Approach

### If Performance is Critical: Option 1 (WAL)

**Best For:**
- High-throughput scenarios (>300 events/sec needed)
- Willing to change architecture
- Long-term solution

**Implementation Plan:**

**Phase 1: WAL Writer (Week 1)**
1. Create WAL file format (position + length + event data)
2. Implement append-only writer with batched fsync
3. Add position index (memory map of position → WAL offset)
4. Test write performance

**Phase 2: WAL Reader (Week 1.5)**
1. Implement WAL reader (scan log, deserialize events)
2. Add WAL compaction (archive old segments)
3. Implement WAL recovery (replay on startup)
4. Test read performance

**Phase 3: Migration (Week 2)**
1. Create migration tool (file-per-event → WAL)
2. Backward compatibility layer (read from both)
3. Gradual migration strategy
4. Documentation

**Phase 4: Testing & Polish (Week 2.5-3)**
1. Concurrent load testing
2. Power failure testing (kill -9 during writes)
3. Recovery testing
4. Performance benchmarks

**Total:** ~3 weeks

**Risk:** Medium-High (architecture change)

**Reward:** 2-3x throughput improvement

---

### If Modest Improvement OK: Option 3 (Background Thread)

**Best For:**
- Want some improvement without major change
- Keep file-per-event architecture
- Quick win

**Implementation Plan:**

**Phase 1: Channel-Based Queue (Day 1-2)**
1. Replace SemaphoreSlim with Channel
2. Create background flush worker thread
3. Test basic queueing

**Phase 2: Batching Logic (Day 3-4)**
1. Implement batch collection (size + timeout)
2. Add graceful shutdown
3. Test batch formation

**Phase 3: Testing (Day 5)**
1. Concurrent load testing
2. Benchmark improvements
3. Edge case testing

**Total:** ~1 week

**Risk:** Low (small change)

**Reward:** 1.3-1.6x improvement (modest)

---

## 📊 Performance Comparison

### Expected Performance

| Approach | Throughput | Latency | Complexity | Risk |
|----------|-----------|---------|------------|------|
| **Current (Immediate)** | 94-130 e/s | 10ms | Low | None |
| **Option 1 (WAL)** | 250-350 e/s | 12-15ms | High | Med-High |
| **Option 2 (Lock-Free)** | 120-180 e/s | 11-13ms | Medium | Medium |
| **Option 3 (Background)** | 120-200 e/s | 11-14ms | Low-Med | Low |

**Recommendation:**
- If need >250 e/s: **Option 1 (WAL)** ✅
- If want quick win: **Option 3 (Background)** ✅
- If unsure: **Keep current** ✅

---

## ⚙️ Technical Details

### WAL File Format

```
WAL Header (64 bytes):
- Magic: "OPOSSUM_WAL_V1" (16 bytes)
- Version: 1 (4 bytes)
- Created: Timestamp (8 bytes)
- Reserved: (36 bytes)

Event Record:
- Position: long (8 bytes)
- Length: int (4 bytes)
- EventType: string (variable)
- EventData: JSON (variable)
- CRC32: uint (4 bytes)

Example:
[Header][Event1][Event2][Event3]...[EventN]
```

### Position Index (In-Memory)

```csharp
private readonly Dictionary<long, WALPointer> _index = new();

struct WALPointer
{
    public long FileOffset;    // Byte offset in WAL file
    public int Length;         // Event data length
}

// Fast lookup: position → WAL offset
var pointer = _index[eventPosition];
var eventData = await ReadWAL(pointer.FileOffset, pointer.Length);
```

### WAL Compaction

```csharp
// When WAL file exceeds 100MB, create new segment
if (_walFile.Length > 100_000_000)
{
    await CreateNewSegment();
    await ArchiveOldSegment(); // Move old WAL to archive/
}

// Keep last 10 segments (configurable)
await PruneOldSegments(keepCount: 10);
```

---

## 🧪 Testing Strategy

### Performance Tests

**Throughput Benchmark:**
```csharp
[Benchmark]
public async Task WAL_Concurrent100Events()
{
    var tasks = Enumerable.Range(0, 100)
        .Select(_ => store.AppendAsync(CreateEvent()));
    await Task.WhenAll(tasks);
}

// Expected: <400ms (250+ events/sec)
```

**Latency Benchmark:**
```csharp
[Benchmark]
public async Task WAL_SingleEvent()
{
    await store.AppendAsync(CreateEvent());
}

// Expected: <15ms (including batching delay)
```

### Reliability Tests

**Power Failure Simulation:**
```csharp
[Fact]
public async Task WAL_SurvivesPowerFailure()
{
    // Append 100 events
    for (int i = 0; i < 100; i++)
        await store.AppendAsync(CreateEvent(i));
    
    // Simulate crash (no graceful shutdown)
    Process.GetCurrentProcess().Kill();
    
    // Restart and verify
    var store2 = new WALEventStore(samePath);
    var events = await store2.ReadAllAsync();
    Assert.Equal(100, events.Length); // All persisted!
}
```

**Concurrent Append Test:**
```csharp
[Fact]
public async Task WAL_ConcurrentAppendsAreConsistent()
{
    var tasks = Enumerable.Range(0, 1000)
        .Select(i => store.AppendAsync(CreateEvent(i)));
    await Task.WhenAll(tasks);
    
    var events = await store.ReadAllAsync();
    Assert.Equal(1000, events.Length);
    Assert.All(events, e => e.Position > 0); // All have valid positions
}
```

---

## 🚨 Risks & Mitigation

### Risk 1: Data Loss on Crash

**Scenario:** Crash between WAL write and fsync

**Mitigation:**
- Periodic background fsync (every 2-10ms)
- On crash, replay WAL from last fsync
- DCB validation catches inconsistencies

### Risk 2: WAL Corruption

**Scenario:** Partial write to WAL file

**Mitigation:**
- CRC32 checksum per event record
- On recovery, skip corrupted records
- Log corruption errors, continue replay

### Risk 3: Performance Regression

**Scenario:** WAL is slower than file-per-event

**Mitigation:**
- Prototype and benchmark before full implementation
- A/B test with current implementation
- Keep fallback to file-per-event

### Risk 4: Migration Complexity

**Scenario:** Existing data can't be migrated

**Mitigation:**
- Dual-mode: Read from both WAL and files
- Gradual migration (background process)
- Keep file-per-event as fallback

---

## 📋 Implementation Checklist

**Before Starting:**
- [ ] Confirmed performance requirement (>200 e/s)
- [ ] Approved architecture change
- [ ] Resources allocated (2-3 weeks)
- [ ] Prototype built and benchmarked

**During Implementation:**
- [ ] WAL file format defined
- [ ] WAL writer implemented
- [ ] WAL reader implemented
- [ ] Position index working
- [ ] Compaction strategy implemented
- [ ] Recovery logic tested
- [ ] Migration tool created
- [ ] Backward compatibility verified

**Testing:**
- [ ] Unit tests pass (>95% coverage)
- [ ] Integration tests pass
- [ ] Concurrent load tests pass
- [ ] Power failure tests pass
- [ ] Performance benchmarks meet targets
- [ ] Memory usage acceptable

**Before Release:**
- [ ] Documentation complete
- [ ] Migration guide written
- [ ] Rollback plan documented
- [ ] Performance compared to baseline
- [ ] Security review (if applicable)

---

## 📚 References

### Similar Implementations

**PostgreSQL WAL:**
- Write-Ahead Logging for durability
- Append-only log with periodic checkpoints
- Our inspiration for Option 1

**Event Store DB:**
- Event-per-file (early versions)
- Switched to append-only log for performance
- Achieved 10x throughput improvement

**SQLite WAL:**
- Write-Ahead Log for ACID transactions
- Batched commits for better performance
- Good reference for our use case

---

## ✅ Success Metrics

**If we implement this, measure:**

1. **Throughput:** >250 events/sec (2.5x current)
2. **Latency:** <15ms per event (50% increase acceptable)
3. **Reliability:** Zero data loss in power failure tests
4. **Complexity:** Code coverage >90%
5. **User Experience:** Backward compatible, easy migration

**If we can't achieve all 5:** Don't ship it!

---

## 🎓 Lessons From Previous Failure

**Don't Repeat These Mistakes:**

1. ❌ Implement without proving need
2. ❌ Ignore architecture mismatch
3. ❌ Use locks in hot path
4. ❌ Benchmark unrealistic scenarios
5. ❌ Ship features that make things worse

**Do These Instead:**

1. ✅ Benchmark current performance first
2. ✅ Prototype before full implementation
3. ✅ Test concurrent scenarios early
4. ✅ Measure, don't guess
5. ✅ Be willing to abandon if it doesn't work

---

**Status:** Planning Only  
**Recommendation:** Keep current implementation unless >200 e/s is required  
**If Implementing:** Use WAL approach (Option 1) for best results
