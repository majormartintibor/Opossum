# Single Context by Design

**Opossum supports exactly one context per application instance. This is a deliberate
design decision, not a temporary limitation.**

See [ADR-004](../decisions/004-single-context-by-design.md) for the full reasoning.

---

## How It Works

```csharp
builder.Services.AddOpossum(options =>
{
    options.RootPath = @"D:\EventStore";

    // ✅ One store — the correct usage
    options.UseStore("CourseManagement");
});
```

Internally, `FileSystemEventStore` and `ProjectionManager` resolve the store path from
`options.StoreName`:

```csharp
var contextPath = GetContextPath(_options.StoreName!);
```

The file system layout is one directory per store name under `RootPath`. This is intentional.

---

## API Enforces Single Store

`UseStore` throws `InvalidOperationException` if called more than once, making the
single-store constraint explicit rather than silent:

```csharp
// ✅ Correct
options.UseStore("CourseManagement");

// ❌ Throws InvalidOperationException
options.UseStore("CourseManagement");
options.UseStore("Billing"); // "UseStore has already been called with 'CourseManagement'..."
```

---

## Working With Multiple Bounded Contexts

If your application genuinely needs two isolated event streams (e.g. `CourseManagement`
and `Billing`), register two separate `IEventStore` instances using .NET named or keyed
services, each pointing to a different root path:

```csharp
// Registration (example using keyed services — .NET 8+)
builder.Services.AddKeyedOpossum("courses", options =>
{
    options.RootPath = @"D:\EventStore\CourseManagement";
    options.UseStore("Main");
});

builder.Services.AddKeyedOpossum("billing", options =>
{
    options.RootPath = @"D:\EventStore\Billing";
    options.UseStore("Main");
});
```
```

Each instance has its own directory, its own ledger, and its own indices. There is no
shared state to co-ordinate. This pattern is explicit, simple, and fully supported by
standard .NET DI without any Opossum-specific multi-context routing logic.
6. **Documentation** - Complete guide on when/how to use multiple contexts

## Developer Guidance

### For Library Users

**For MVP, configure EXACTLY ONE context:**

```csharp
✅ DO THIS:
builder.Services.AddOpossum(options =>
```csharp
// ✅ DO THIS:
builder.Services.AddOpossum(options =>
{
    options.RootPath = @"D:\EventStore";
    options.UseStore("CourseManagement");
});

// ❌ DON'T DO THIS (throws InvalidOperationException):
builder.Services.AddOpossum(options =>
{
    options.RootPath = @"D:\EventStore";
    options.UseStore("CourseManagement");
    options.UseStore("Billing");   // throws: "UseStore has already been called..."
});
```

### For Library Contributors

When working on Opossum codebase:

1. **Always use `_options.StoreName!`** — single store, always set after startup validation
2. **Do not add multi-context routing** — see ADR-004 for why this is by design
3. **Write tests for single store** — multi-store scenarios use separate DI registrations

## See Also

- [ADR-004](../decisions/004-single-context-by-design.md) — Single-context-by-design decision record
- [Configuration Guide](../guides/configuration-guide.md) *(planned)* — How to configure Opossum

---

**Status:** By design — single store per instance enforced in API
