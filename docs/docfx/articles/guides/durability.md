<!-- source: docs/guides/durability-quick-reference.md � keep in sync -->

# Quick Reference: Durability Configuration

## TL;DR 🎯

**Production:** Keep default (flush = true)  
**Testing:** Set `FlushEventsImmediately = false` for speed

---

## Configuration

```csharp
// Production (default) - Maximum safety
builder.Services.AddOpossum(options =>
{
    options.RootPath = "D:\\Database";
    options.UseStore("Production");
    // FlushEventsImmediately = true (default)
});

// Testing - Skip flush for speed
builder.Services.AddOpossum(options =>
{
    options.RootPath = "TestData";
    options.UseStore("TestContext");
    options.FlushEventsImmediately = false; // ← 2-3x faster tests
});
```

---

## What Gets Flushed?

| Data Type | Flush? | Why |
|-----------|--------|-----|
| Events | ✅ YES | Source of truth |
| Ledger | ✅ YES | Sequence integrity |
| Projections | ❌ NO | Rebuildable |
| Indices | ❌ NO | Rebuildable |

---

## Performance Impact

**With Flush (Production):**
- Event append: ~1-5ms
- Throughput: ~200-1000 events/sec (NVMe SSD)
- **Guaranteed durability** ✅

**Without Flush (Testing):**
- Event append: ~0.1-0.5ms
- Throughput: ~2000-10000 events/sec
- **Risk of data loss** ⚠️

---

## Decision Tree

```
Do you need guaranteed durability?
├─ YES (production) → Keep default (flush = true)
├─ NO (testing) → Set FlushEventsImmediately = false
└─ UNSURE → Use default (safe choice)
```

---

## When to Disable Flush

✅ **Safe to disable:**
- Unit tests
- Integration tests
- Local development
- Throwaway data

❌ **NEVER disable in:**
- Production
- Staging (if mirrors production)
- Any environment with important data

---

## Example: Test Configuration

```csharp
public class TestFixture
{
    private readonly IEventStore _eventStore;
    
    public TestFixture()
    {
        var options = new OpossumOptions
        {
            RootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
            FlushEventsImmediately = false // ← Faster tests
        };
        options.UseStore("TestContext");
        
        _eventStore = new FileSystemEventStore(options);
    }
}
```

---

## Troubleshooting

### Tests Slower After Upgrade?
```csharp
// Add this to test setup
options.FlushEventsImmediately = false;
```

### Production Data Loss?
```csharp
// Verify production config
options.FlushEventsImmediately = true; // ← Must be true!
```

### Performance Issues?
- Check storage type (HDD vs SSD)
- Consider Phase 3 features (batch flushing, WAL)
- Monitor with metrics (future feature)

---

For full details, see the [implementation notes on GitHub](https://github.com/majormartintibor/Opossum/blob/master/docs/implementation/durability-guarantees.md).
