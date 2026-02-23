# ADR-004: Single Context by Design — Multi-Context Support Will Not Be Implemented

**Date:** 2026-03-01
**Status:** ❌ Closed — Will Not Implement

---

## Context

The 0.3.0-preview.1 roadmap (item 1) proposed adding multi-context support to `IEventStore`
and `IProjectionManager` — the ability to route reads, appends, and projection rebuilds to
one of several named contexts configured on a single Opossum instance. The motivation was
bounded context isolation analogous to having separate database schemas within one PostgreSQL
database, a pattern associated with the modern modular monolith architecture.

After evaluating this against Opossum's actual deployment profile, the feature was closed.

---

## What Multi-Context Would Have Looked Like

The proposed API added an optional `context` parameter to every read and append:

```csharp
// New multi-context callers opt in explicitly
await eventStore.AppendAsync(events, condition, context: "Billing");
await eventStore.ReadAsync(Query.All(), context: "Inventory");
```

`ProjectionManager` would have gained corresponding `context` parameters on
`RegisterProjection`, `RebuildAsync`, and `UpdateAsync`. Internally, a
`ConcurrentDictionary<string, SemaphoreSlim>` would have replaced the single
`_appendLock` to avoid serialising appends across unrelated contexts.
`ProjectionDaemon` would have polled each registered context separately.

The storage layout already supports it — `StorageInitializer` creates directories
per context name — so the work was purely in the routing and locking layers.

---

## Why It Was Rejected

### 1. Wrong pattern for Opossum's deployment profile

Multi-context-in-one-process is a cloud-era **modular monolith** pattern: one deployable
unit, multiple internal bounded contexts, a shared infrastructure layer. It is designed
for horizontally scaled cloud services where running separate processes per context has
real operational cost (separate CI pipelines, separate Kubernetes deployments, separate
monitoring stacks).

Opossum is designed for the opposite end of the spectrum:

> Perfect for scenarios where simplicity, offline operation, and local data sovereignty
> matter more than cloud scalability.
>
> ✅ POS systems · ✅ Field service · ✅ SMB ERP · ✅ Offline-first · ✅ Single server

In a POS system or a field service application, you deploy **one focused application** to
one device or one site. That application models one bounded context. There is no operational
overhead for "running a separate process" — it is just the application. Multi-context adds
complexity to solve a problem that does not exist in this profile.

### 2. The workaround is already functional and clean

A consumer who genuinely needs two isolated contexts can register two `IEventStore`
instances with different root paths using standard .NET named or keyed service
registration. Each instance operates independently, has its own file system directory,
its own ledger, and its own index. There is no shared state to co-ordinate. This is
simpler and more explicit than a shared instance with a routing layer.

### 3. Implementation complexity is disproportionate

The required changes touched every layer of the stack:

| Component | Change required |
|-----------|----------------|
| `IEventStore` | New optional `context` parameter on `ReadAsync` and `AppendAsync` |
| `FileSystemEventStore` | Replace single `SemaphoreSlim` with `ConcurrentDictionary<string, SemaphoreSlim>` |
| `IProjectionManager` | New `context` parameter on `RegisterProjection`, `RebuildAsync`, `UpdateAsync` |
| `ProjectionManager` | Per-context checkpoint paths, per-context locks |
| `ProjectionDaemon` | Poll each registered context independently |
| `IEventStore` extensions | All overloads gain a `context` parameter |
| All tests | Every read/append call site potentially affected |

All of this complexity — per-context semaphores, routing logic, projection scoping,
daemon polling loops — delivers zero benefit for any of Opossum's documented use cases.

### 4. It does not align with DCB's model

The DCB specification models events across a single logical store per deployment.
Consistency boundaries are expressed through event types and tags, not through
physical storage partitions. Introducing multiple contexts in one store blurs the
boundary between "routing" (an infrastructure concern) and "querying" (a domain concern)
in a way that is not addressed by the spec.

---

## Current State of the API (as of 0.3.0-preview.1)

`OpossumOptions` exposes `UseStore(string name)` which sets the single `StoreName`
property. Calling `UseStore` a second time throws `InvalidOperationException` immediately,
making the single-store contract explicit at the call site rather than silent:

```csharp
// ✅ Correct — one store per instance
builder.Services.AddOpossum(options =>
{
    options.RootPath = @"D:\EventStore";
    options.UseStore("CourseManagement");
});

// ❌ Throws InvalidOperationException at startup
builder.Services.AddOpossum(options =>
{
    options.RootPath = @"D:\EventStore";
    options.UseStore("CourseManagement");
    options.UseStore("Billing"); // throws: "UseStore has already been called..."
});
```

`ContextNotFoundException` also exists in the public API, having been created in
anticipation of multi-context support. It will not be thrown by the framework under any
current code path. It is retained to avoid a breaking removal but carries no active
routing logic.

---

## If the Deployment Profile Changes

If Opossum is ever adopted in scenarios where multi-context genuinely matters — for
example a cloud-hosted multi-tenant SaaS application — this decision should be
re-evaluated from scratch. At that point, the architecture would likely differ
significantly from the current file-per-event model anyway.

This decision is closed and is not on any future roadmap.
