# Opossum Use Cases: When a File System Event Store Makes Sense

## Executive Summary

Opossum is a file system-based event store for .NET. Each event is stored as an individual JSON file on disk, with tag and event-type indices for querying and a projection system for materialized views.

This document describes scenarios where Opossum's architecture — **no database dependency, fully offline, local storage only** — is a reasonable fit. These are recommended use cases based on the library's design constraints and benchmarked performance. **Opossum has not been deployed in production.** The only working application is the CourseManagement sample included in the repository.

### What Opossum provides

- Append-only event store with durable writes (fsync)
- Tag-based and event-type-based indexing
- Materialized projections with incremental updates
- DCB (Dynamic Consistency Boundaries) for optimistic concurrency
- Cross-process file locking for multi-instance safety
- OpenTelemetry tracing
- No external dependencies — just the file system

### What Opossum does NOT provide

- No built-in encryption or cryptographic integrity
- No per-event deletion (only whole-store delete via `DeleteStoreAsync`)
- No data sync or replication between nodes
- No access control beyond OS file permissions
- No digital signatures or tamper detection
- No branching or versioning of event streams

---

## Recommended Use Cases

The following scenarios are architecturally compatible with Opossum's throughput limits (~55 durable events/sec, < 100,000 events/day) and single-server design. They are based on analysis of the library's capabilities, not production experience.

### 1. Low-Volume Business Applications (On-Premises)

**Example:** Car dealership sales and commission tracking  
**Environment:** On-premises Windows or Linux server  
**Scale:** A few hundred events per day at most

#### Why this fits

A small dealership might record 5-20 vehicle sales per day, each generating a handful of events (sale, commission, trade-in, financing). That's well under 100 events/day — far below Opossum's throughput ceiling. The projection system can maintain read models for commission summaries, monthly reports, and audit views.

| Characteristic | Opossum fit |
|---|---|
| Event volume | Excellent — tens to hundreds per day |
| Offline operation | Built-in — no network dependency |
| Audit trail | Append-only event files on local disk |
| Data stays on-premise | Inherent — it's just files |
| No database needed | Core design principle |

#### Architecture sketch

```
Dealership Event Store
├── Events/
│   ├── VehicleSold, CommissionAdjusted
│   ├── TradeInProcessed, FinancingArranged
│
├── Projections/
│   ├── MonthlySalesReport
│   ├── SalespersonCommissionSummary
│
└── Indices/
    └── Tags: SalespersonId, Month, Year
```

#### Code example

```csharp
// Projection: Calculate total commission for a salesperson
[ProjectionDefinition("SalespersonCommissions")]
[ProjectionTags(typeof(CommissionTagProvider))]
public sealed class SalespersonCommissionProjection : IProjectionDefinition<CommissionSummary>
{
    public string ProjectionName => "SalespersonCommissions";

    public string[] EventTypes =>
    [
        nameof(VehicleSoldEvent),
        nameof(CommissionAdjusted)
    ];

    public string KeySelector(SequencedEvent evt) =>
        evt.Event.Tags.First(t => t.Key == "SalespersonId").Value;

    public CommissionSummary? Apply(CommissionSummary? current, SequencedEvent evt)
    {
        return evt.Event.Event switch
        {
            VehicleSoldEvent sold when current is not null => current with
            {
                TotalCommission = current.TotalCommission +
                    (sold.SalePrice * sold.CommissionRate),
                SalesCount = current.SalesCount + 1
            },
            CommissionAdjusted adjusted when current is not null => current with
            {
                TotalCommission = adjusted.NewCommissionAmount
            },
            _ => current
        };
    }
}
```

#### Honest limitations

- Opossum provides no reporting UI — you need to build that yourself
- No built-in backup or replication; you must handle file-level backups externally
- If the dealership grows to a multi-location chain with shared data, Opossum has no sync mechanism

---

### 2. Factory Production Logging (No-Database Environments)

**Example:** Assembly line audit log on a factory floor  
**Environment:** Industrial PC (Windows IoT or Linux)  
**Scale:** Hundreds to low thousands of events per shift  
**Constraint:** Company policy prohibits database installation on OT networks

#### Why this fits

Some manufacturing environments ban databases on Operational Technology (OT) networks due to security policy, licensing costs, or patching requirements. Opossum stores plain JSON files — no database process, no SQL, no network listener. If the factory generates a few hundred production events per shift (unit started, station completed, quality check performed), that's well within limits.

| Characteristic | Opossum fit |
|---|---|
| No database required | Core design — just files |
| Event volume | Good — hundreds per shift |
| Air-gapped network | Works fully offline |
| Post-incident analysis | Replay events to debug |
| Shift reports | Projections for aggregated views |

#### What Opossum is NOT suitable for in this scenario

- **Real-time robot coordination**: At ~55 durable events/sec (~18 ms per write), Opossum is too slow for real-time command-and-control loops. Use industrial protocols (OPC UA, MQTT, EtherCAT) for robot communication.
- **High-frequency sensor data**: If 50 workstations each generate multiple events per second, you'll exceed Opossum's throughput. Use a time-series store or message broker for high-frequency data.
- **SCADA/MES integration**: Opossum has no built-in protocol adapters. Integration would require custom code to read/write events.

#### Reasonable scope

Opossum works as a **production audit log and reporting back-end** — recording discrete business events (unit started, quality check passed, shift completed) and building summary projections for shift reports and quality dashboards.

#### Code example

```csharp
// Projection: Track unit progress through assembly stations
[ProjectionDefinition("UnitProgress")]
[ProjectionTags(typeof(UnitProgressTagProvider))]
public sealed class UnitProgressProjection : IProjectionDefinition<UnitProgress>
{
    public string ProjectionName => "UnitProgress";

    public string[] EventTypes =>
    [
        nameof(UnitStartedEvent),
        nameof(StationCompletedEvent),
        nameof(DefectDetectedEvent)
    ];

    public string KeySelector(SequencedEvent evt) =>
        evt.Event.Tags.First(t => t.Key == "UnitId").Value;

    public UnitProgress? Apply(UnitProgress? current, SequencedEvent evt)
    {
        return evt.Event.Event switch
        {
            UnitStartedEvent started => new UnitProgress
            {
                UnitId = started.UnitId,
                ProductType = started.ProductType,
                CurrentStation = "Station1",
                Status = "InProgress"
            },

            StationCompletedEvent completed when current is not null => current with
            {
                CurrentStation = completed.NextStation,
                CompletedStations = current.CompletedStations + 1
            },

            DefectDetectedEvent defect when current is not null => current with
            {
                Status = "DefectDetected",
                DefectType = defect.DefectType
            },

            _ => current
        };
    }
}

// Query: All events for a specific production unit
var unitEvents = await eventStore.ReadAsync(
    Query.FromTags(new Tag("UnitId", unitId.ToString())),
    readOptions: null);
```

---

### 3. Desktop Applications with Event-Sourced State

**Example:** A desktop tool that benefits from undo/redo and full history  
**Environment:** Single user, local machine  
**Scale:** Hundreds to thousands of events per session

#### Why this fits

Event sourcing is a natural fit for applications that need unlimited undo/redo, time-travel debugging, or a complete change history. A desktop application with a single user generates very low event volume. Opossum's file-based storage means no external service dependencies.

| Characteristic | Opossum fit |
|---|---|
| Event volume | Excellent — low hundreds per session |
| Offline operation | Inherent — everything is local |
| Undo/redo | Replay events to reconstruct any prior state |
| Change history | Every action recorded with timestamp |
| No external dependencies | Just files on disk |

#### Honest limitations

- Opossum stores events in a directory tree (not a single portable file). Sharing a "project" means zipping or copying the entire directory.
- No built-in branching or versioning — if you need design branches, you'd have to implement that on top of Opossum.
- For very high-frequency interactions (e.g., every mouse movement), you'd need to batch/debounce before appending.

---

### 4. Prototyping and Learning Event Sourcing

**Example:** A developer learning event sourcing patterns  
**Environment:** Local development machine  
**Scale:** Any (development/test volumes)

#### Why this fits

Opossum requires zero infrastructure — no Docker containers, no database servers, no cloud accounts. Install the NuGet package and start writing events. The DCB pattern, projection system, and mediator are all available immediately. This makes it a low-friction way to learn and experiment with event sourcing concepts.

The CourseManagement sample application demonstrates:
- Event design and domain events
- Projections with tag-based indexing
- DCB concurrency guards (read → decide → append)
- Aggregate patterns
- Mediator-based command/query handling

---

## When NOT to Use Opossum

❌ **High-throughput applications**
- Opossum sustains ~55 durable events/sec. Web APIs, e-commerce, or SaaS platforms that need thousands of events/sec require a proper event store (EventStoreDB) or message broker (Kafka).

❌ **Real-time systems**
- At ~18 ms per durable write, Opossum is not suitable for real-time control loops, game servers, or high-frequency trading.

❌ **Systems requiring encryption or compliance certification**
- Opossum has no built-in encryption, no digital signatures, no cryptographic integrity checks, and no per-event deletion. If you need HIPAA, PCI-DSS, SOX, or FDA 21 CFR Part 11 compliance, you need a system with those features built in. OS-level encryption (BitLocker) and file permissions are not a substitute for application-level security controls.

❌ **Multi-node or distributed systems**
- Opossum is single-server only. No replication, no clustering, no sync between nodes.

❌ **Systems that need to delete individual events**
- Opossum is strictly append-only. The only deletion operation (`DeleteStoreAsync`) wipes the entire store. If you need GDPR right-to-erasure at the event level, Opossum does not support this.

❌ **Cloud-native / containerized deployments**
- Shared file systems across Kubernetes pods or serverless functions are fragile. Opossum assumes a stable local disk.

❌ **IoT or sensor data collection**
- High-frequency sensor readings can easily exceed Opossum's throughput limits. Use a time-series database (InfluxDB, TimescaleDB) or message broker (MQTT + Kafka) instead.

---

## Decision Framework: Should I Use Opossum?

### Choose Opossum if:

- **Scale:** < 100,000 events/day (~1 event/second average)
- **Deployment:** Single server, single application instance
- **Network:** Offline operation required or unreliable connectivity
- **Infrastructure:** Cannot or prefer not to install a database
- **Team:** Small team, no DBA or cloud expertise
- **Budget:** Cannot afford database licenses or cloud fees
- **Use case:** Event sourcing benefits (audit trail, replay, projections) at low volume

### Choose a dedicated event store or database if:

- **Scale:** > 100,000 events/day
- **Deployment:** Multi-region, high availability, or horizontal scaling
- **Security:** Need encryption, access control, or compliance certification
- **Data lifecycle:** Need per-record deletion or retention policies
- **Operations:** Need replication, backup automation, or monitoring built in

---

## Performance Characteristics

> All numbers below come from the [2026-03-11 BenchmarkDotNet run](../../benchmarking/results/20260311/)
> on Windows 11, .NET 10.0.2, SSD storage. See `docs/performance/PERFORMANCE-BASELINE.md` for
> the full dataset and methodology.

### Benchmarked Throughput (Single Server, SSD)

| Operation | Throughput | Latency | Notes |
|-----------|-----------|---------|-------|
| **Append (durable, single event)** | ~55 events/sec | ~18 ms | `FlushEventsImmediately = true` — fsync per event |
| **Append (durable, batch of 10)** | ~78 events/sec | ~13 ms/event | Amortised over batch |
| **Append (no flush)** | ~185 events/sec | ~5.4 ms | OS page cache only — data-loss risk on power failure |
| **Tag query (high selectivity)** | — | ~524 μs | Index-based, few matches |
| **Tag query (1K events)** | — | ~11.6 ms | Sub-linear scaling |
| **ReadLast (100 → 10K events)** | — | 948–1,158 μs | Near-O(1): one index lookup + one file read |
| **Read by EventType (10K events)** | — | ~206 ms | Index-based |
| **Projection rebuild** | — | ~4.5 ms / 50 events | Write-through; bounded memory |
| **Incremental projection update** | — | ~4.6 μs / 0 B alloc | ~978× faster than full rebuild |

### Scalability Limits

| Metric | Recommended Limit | Notes |
|--------|------------------|-------|
| **Events per day** | < 100,000 | ~1 event/second average |
| **Total events** | < 10 million | Performance degrades with file count |
| **Projections** | < 100 types | More = slower startup |
| **Tags per event** | < 20 | Affects index write speed |
| **Concurrent appends** | < 100 simultaneous | File system lock contention |

**Beyond these limits?** Consider cloud-based event stores (EventStoreDB, Azure Event Hubs).

### Optimization Tips

1. **Use SSD storage** — flush operations are much faster (10 ms vs 50 ms+ on HDD)
2. **Use tag-based queries** — ~524 μs for high selectivity vs ~5.3 ms for broader queries
3. **Enable parallel projection rebuilding** — `MaxConcurrentRebuilds` config; Concurrency=4 is ~47 % faster than sequential
4. **Use incremental projection updates** — ~978× faster than full rebuild; zero allocation
5. **Batch writes** — append multiple events in one transaction for better throughput

---

## Support & Resources

### GitHub Repository
- **Source Code:** https://github.com/majormartintibor/Opossum
- **Issues & Discussions:** Report bugs, request features
- **Samples:** CourseManagement sample application

### Getting Help
1. Check the CourseManagement sample application code
2. Review integration tests for usage patterns
3. Open a GitHub issue for bugs
4. Use Discussions for architecture questions

---

## Conclusion

Opossum is a good fit for **low-volume, single-server applications** where event sourcing provides value (audit trails, projections, replay) and where simplicity and offline operation outweigh the need for scalability, encryption, or distributed features.

It is **not** a general-purpose event store and should not be used in scenarios that require high throughput, encryption, compliance certification, data sync, or multi-node deployment.

**Key question to ask:** *Does my application generate fewer than ~100,000 events per day, run on a single server, and not require built-in encryption or data sync?* If yes, Opossum may be a good fit.

---

**Last Updated:** 2026  
**Status:** Pre-production (sample application only — no production deployments)
