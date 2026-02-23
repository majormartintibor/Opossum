# .NET Event Sourcing Framework Comparison

> **Purpose:** Honest competitive analysis to inform Opossum's strategic direction.
> This document is intentionally blunt. It is not marketing material.
>
> **Last updated:** 2026

---

## Frameworks Covered

| Framework | Backing Store | Created by |
|---|---|---|
| **Opossum** | File system (JSON) | This repo |
| **EventStoreDB (ESDB)** | Dedicated server (custom binary log) | Event Store Ltd. |
| **Marten** | PostgreSQL | JasperFx / OSS community |
| **Eventuous** | ESDB / PostgreSQL / InMemory | Alexey Zimarev |
| **NEventStore** | SQL / MongoDB / others | OSS community (maintenance mode) |
| **SqlStreamStore** | MSSQL / PostgreSQL / MySQL | OSS community |

---

## Full Feature Matrix

| Capability | Opossum | EventStoreDB | Marten | Eventuous | NEventStore | SqlStreamStore |
|---|---|---|---|---|---|---|
| **Storage backend** | File system (JSON) | Dedicated server | PostgreSQL | ESDB / PG | SQL / Mongo | SQL variants |
| **Infrastructure required** | ❌ None | ✅ ESDB server | ✅ PostgreSQL | ✅ Backend server | ✅ DB server | ✅ DB server |
| **Embedded / in-process** | ✅ Yes | ❌ No | ❌ No | ❌ No | ❌ No | ⚠️ InMemory only |
| **Offline operation** | ✅ Yes | ❌ No | ❌ No | ❌ No | ❌ No | ❌ No |
| **Multi-process safe** | ❌ No | ✅ Yes | ✅ Yes | ✅ Yes | ✅ Yes | ✅ Yes |
| **Multi-node / HA** | ❌ No | ✅ Cluster | ✅ PG replication | ✅ Backend-dependent | ⚠️ Partial | ⚠️ Partial |
| **Throughput (approx.)** | ~100–220 events/sec | ~50k–100k+/sec | ~10k–50k+/sec | Backend-dependent | Backend-dependent | Backend-dependent |
| **DCB compliant** | ✅ First-class | ⚠️ Manual | ⚠️ Stream-scoped only | ✅ Explicit | ❌ No | ❌ No |
| **Cross-stream consistency** | ✅ Yes (DCB) | ⚠️ Hard / manual | ❌ No | ✅ Yes | ❌ No | ❌ No |
| **Optimistic concurrency** | ✅ Tag+type scoped | ✅ Stream-position | ✅ Stream-version | ✅ Yes | ✅ Yes | ✅ Yes |
| **Projections (built-in)** | ✅ Polling daemon | ✅ Server-side + catchup | ✅ Async daemon | ✅ Yes | ⚠️ Basic | ❌ No |
| **Real-time subscriptions** | ❌ Polling only | ✅ Yes (persistent + catchup) | ✅ Async daemon | ✅ Yes | ⚠️ Limited | ✅ Yes |
| **IAsyncEnumerable reads** | ❌ Array load only | ✅ Yes | ✅ Yes | ✅ Yes | ❌ No | ✅ Yes |
| **Snapshots** | ❌ No | ✅ Yes | ✅ Yes | ✅ Yes | ✅ Yes | ❌ No |
| **Schema evolution / upcasting** | ❌ No | ✅ Yes | ✅ Yes | ✅ Yes | ⚠️ Manual | ❌ No |
| **Tag / label based querying** | ✅ First-class | ⚠️ Metadata only | ⚠️ Via metadata | ⚠️ Via metadata | ❌ No | ❌ No |
| **Built-in mediator** | ✅ Yes | ❌ No | ❌ No | ✅ Yes | ❌ No | ❌ No |
| **OpenTelemetry / tracing** | ✅ ActivitySource | ✅ Yes | ✅ Yes | ✅ Yes | ❌ No | ❌ No |
| **Maturity** | Alpha (0.2.x) | Production (decades) | Production (years) | Beta / Production | Maintenance mode | Stable, low activity |
| **Licensing** | MIT | Mixed (community + paid) | MIT | MIT | MIT | MIT |
| **NuGet downloads** | Very low (new) | Very high | Very high | Growing | High (legacy) | Moderate |

---

## The Unique Space Opossum Occupies

The only space Opossum occupies uniquely is the intersection of all three:

```
Zero infrastructure  ∩  DCB compliant  ∩  Embedded in-process
```

No other .NET framework covers this combination. That intersection is real, but the set of
production applications that genuinely need all three is small.

---

## Brutally Honest Assessment of Opossum's Weak Spots

### 1. The throughput ceiling disqualifies web and high-throughput scenarios

~100–220 events/sec with `FlushEventsImmediately = true` is an absolute ceiling even
on a single machine. A busy web API with a few dozen concurrent users will blow through
this within seconds. Marten on a local PostgreSQL does 10,000+. This is not a gap that
can be closed by optimizing the current file-per-event design; it is structural.

**However, this ceiling is completely irrelevant for Opossum's actual target audience.**
A car dealership with 5 workstations where staff submit one form every 30 seconds peaks
at ~0.17 events/sec — less than 0.2% of the ceiling. A POS terminal processing one
transaction every 5 seconds across 10 terminals is ~2 events/sec. The throughput
limitation only matters the moment you consider a web API or any high-concurrency
scenario. For SMB, desktop, and offline use cases, the ceiling is not a practical constraint.

### 2. Single-process is a hard architectural wall

The `SemaphoreSlim` + file locks only work within one OS process. The moment you scale
to two app instances (blue/green deploy, Kubernetes, even IIS worker recycling with
overlap) you have a race condition with no protection. This is not a roadmap item —
it is structural to the file system backend.

### 3. No streaming — everything is loaded into memory

`ReadAsync` returns `SequencedEvent[]`. On a store with 500k events queried broadly,
that is a full in-memory load. There is no `IAsyncEnumerable<SequencedEvent>` path.
This is a correctness and stability risk for long-running stores, not just a performance issue.
(See [ADR-003](../decisions/003-iasyncenumerable-not-implemented.md) for the deferred decision.)

### 4. No schema evolution story

When `CourseCreatedEvent` gains a new required field in v2, there is currently no
upcasting pipeline, no versioning strategy, no migration tooling. Every event store faces
this eventually; most have a documented answer. Opossum does not yet.

### 5. DCB compliance is genuine, but not indefinitely unique

Eventuous is explicitly DCB-aware. The DCB spec itself is young and its author actively
publishes reference implementations. Opossum's DCB implementation is correct, but it will
not remain the only DCB-compliant embedded option forever.

---

## Realistic Use-Case Fit

| Use Case | Fit | Honest Caveat |
|---|---|---|
| **Desktop / WPF / WinForms app with audit trail** | ✅ Strong | Best option when no server is available |
| **CLI tools / background services (single process)** | ✅ Strong | Perfect fit — zero setup |
| **Embedded IoT / edge device (offline, single process)** | ✅ Strong | Real differentiator vs. all alternatives |
| **Development & local testing (no Docker)** | ✅ Strong | Excellent DX — `dotnet run` and you're done |
| **Learning DCB / event sourcing** | ✅ Strong | Low ceremony, clean concepts, good sample app |
| **POS / dealership / SMB (single server, single app instance)** | ✅ Strong | Throughput ceiling is irrelevant in practice (real business event rates are orders of magnitude below it); the only hard constraint is **single app instance** — two servers writing simultaneously corrupts data |
| **Web API with any meaningful load** | ❌ Poor fit | Throughput ceiling and no multi-instance safety |
| **Microservices / distributed systems** | ❌ Wrong tool | No cross-process safety whatsoever |
| **High-audit compliance workloads** | ⚠️ Risky | No built-in backup, no upcasting, no schema migration |
| **Event streaming to other services** | ❌ No | Polling only, no push subscriptions |

---

## The Real Competition

Opossum's honest competition is **not** EventStoreDB or Marten.

In the niche Opossum targets (desktop, edge, CLI, SMB, offline), developers typically
reach for **SQLite** or a hand-rolled append-only log. They do not use event sourcing at
all, because every existing event sourcing library requires a server.

Opossum's core value proposition to that audience is:
> *"You can have a proper event store — with DCB concurrency, projections, and an
> immutable audit trail — without running any server. Just files."*

For that pitch, the DCB story and the built-in projection daemon are the strongest
selling points. The comparison to EventStoreDB or Marten is largely irrelevant for this
audience.

---

## What Would Make Opossum Broadly Applicable

The framework becomes significantly more broadly applicable if any of the following are
delivered:

| Investment | What it unlocks |
|---|---|
| `IAsyncEnumerable<SequencedEvent>` reads | Correctness for large stores; removes in-memory ceiling |
| SQLite or LMDB backend option | Multi-process safety while preserving zero-infrastructure story |
| Upcasting / schema evolution pipeline | Required for production longevity of any real application |
| Push-based subscriptions (file system watch) | Eliminates polling overhead; enables reactive projections |
| Snapshot support | Required for aggregates with long event streams |
