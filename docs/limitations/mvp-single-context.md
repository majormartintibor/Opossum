# MVP Limitation: Single Context Only

## Current Status (MVP)

**Opossum currently supports ONLY ONE context per application instance.**

While the API allows adding multiple contexts via `options.AddContext()`, the implementation currently uses only the **first context** that was added. All events are stored in and retrieved from this first context.

## How It Works Now

```csharp
builder.Services.AddOpossum(options =>
{
    options.RootPath = @"D:\EventStore";
    
    // ✅ SUPPORTED: Single context
    options.AddContext("CourseManagement");
    
    // ⚠️ IGNORED: Additional contexts are accepted but NOT used
    // options.AddContext("Billing");       // This will be ignored
    // options.AddContext("Inventory");     // This will be ignored
});
```

### What Happens Internally

When you append or query events, Opossum uses `_options.Contexts[0]` to determine the storage path:

```csharp
// From FileSystemEventStore.cs
var contextPath = GetContextPath(_options.Contexts[0]); // Only first context is used
```

## Why This Limitation Exists

This is an intentional MVP (Minimum Viable Product) limitation to:

1. **Keep initial implementation simple** - Multi-context support requires:
   - Context selection API in commands/queries
   - Context routing logic throughout the stack
   - Cross-context query capabilities
   - Additional validation and error handling

2. **Validate core functionality first** - Before adding complexity, we want to ensure:
   - Single-context DCB implementation is solid
   - Performance characteristics are well understood
   - API design is intuitive

3. **Avoid premature abstraction** - Real-world usage will inform the best API for multi-context support

## Current Validation

### ✅ Validation That Works

```csharp
// ❌ This correctly throws an exception
options.AddContext("Context1");
options.AddContext("Context1"); // InvalidOperationException: Context already exists

// ❌ This correctly throws an exception  
// No context added - validation fails
builder.Services.AddOpossum(options => { }); // InvalidOperationException: At least one context required
```

### ⚠️ What Validation is Missing

```csharp
// ⚠️ This is ACCEPTED but only "Context1" is actually used
options.AddContext("Context1");
options.AddContext("Context2"); // No error, but Context2 is never used
options.AddContext("Context3"); // No error, but Context3 is never used
```

**There is currently NO validation to enforce single-context-only in the MVP.**

## Workaround for Multiple Bounded Contexts

If you need to separate multiple bounded contexts in your application, you have two options:

### Option 1: Separate Application Instances (Recommended)

Run separate instances of your application, each with its own context:

```csharp
// CourseManagement Service (Process 1)
builder.Services.AddOpossum(options =>
{
    options.RootPath = @"D:\EventStore";
    options.AddContext("CourseManagement");
});

// Billing Service (Process 2)
builder.Services.AddOpossum(options =>
{
    options.RootPath = @"D:\EventStore";
    options.AddContext("Billing");
});
```

This follows microservices principles and gives you:
- ✅ True isolation between contexts
- ✅ Independent scaling
- ✅ Separate deployment

### Option 2: Separate Root Paths (Not Recommended)

Use different root paths for each bounded context within the same application:

```csharp
// ⚠️ This works but defeats the purpose of bounded contexts
builder.Services.AddOpossum(options =>
{
    options.RootPath = @"D:\EventStore\CourseManagement";
    options.AddContext("Main");
});

// Register another IEventStore for Billing
// (Requires custom DI registration - not officially supported in MVP)
```

**Why this is not recommended:**
- Requires custom dependency injection setup
- No official support for multiple IEventStore instances
- Confusing API - contexts are meant to be within the same root

## Planned Future Support

Multi-context support is planned for a future release. The proposed API would look like:

```csharp
// Future API (not yet implemented)
public class CreateCourseCommand : ICommand
{
    [Context("CourseManagement")] // Attribute to specify context
    public required string AggregateId { get; init; }
    // ...
}

// OR context passed in command metadata
await mediator.SendAsync(new CreateCourseCommand { ... }, 
    metadata: new CommandMetadata { Context = "CourseManagement" });
```

### Requirements for Full Multi-Context Support

1. **Context selection API** - Way to specify which context to use per command/query
2. **Context validation** - Ensure specified context exists in configuration
3. **Storage path resolution** - Resolve correct storage path based on selected context
4. **Cross-context queries** - Ability to query across multiple contexts (optional)
5. **Projection context tracking** - Projections need to know which context they're reading from
6. **Documentation** - Complete guide on when/how to use multiple contexts

## Developer Guidance

### For Library Users

**For MVP, configure EXACTLY ONE context:**

```csharp
✅ DO THIS:
builder.Services.AddOpossum(options =>
{
    options.RootPath = @"D:\EventStore";
    options.AddContext("CourseManagement"); // Only one context
});

❌ DON'T DO THIS (won't work as expected):
builder.Services.AddOpossum(options =>
{
    options.RootPath = @"D:\EventStore";
    options.AddContext("CourseManagement");
    options.AddContext("Billing");          // Will be ignored!
    options.AddContext("Inventory");        // Will be ignored!
});
```

### For Library Contributors

When working on Opossum codebase:

1. **Always use `_options.Contexts[0]`** - This makes the single-context limitation obvious
2. **Add TODO comments** - Mark areas that will need changes for multi-context support
3. **Write tests for single context** - Don't test multi-context scenarios yet
4. **Document the limitation** - Be explicit in XML comments and error messages

## See Also

- [DCB Specification](../../Specification/DCB-Specification.md) - Understanding bounded contexts
- [Use Cases](../guides/use-cases.md) - When to use separate contexts
- [Configuration Guide](../guides/configuration-guide.md) *(planned)* - How to configure Opossum

---

**Last Updated:** 2026-02-07  
**Status:** MVP Limitation - Planned for future release
