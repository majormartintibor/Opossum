# 🦝 Opossum — File System Event Store

**A .NET library that turns your file system into a fully functional event store.**

Opossum implements the [DCB (Dynamic Consistency Boundaries)](https://dcb.events) specification — bringing event sourcing, optimistic concurrency, projections, and the mediator pattern to any .NET application without a database server.

[![NuGet](https://img.shields.io/nuget/v/Opossum.svg)](https://www.nuget.org/packages/Opossum/)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
[![License](https://img.shields.io/github/license/majormartintibor/Opossum.svg)](https://github.com/majormartintibor/Opossum/blob/master/LICENSE)
[![Docs](https://img.shields.io/badge/docs-gh--pages-blue)](https://majormartintibor.github.io/Opossum/)

---

## Install

```bash
dotnet add package Opossum
```

---

## 30-Second Example

```csharp
// Register
builder.Services.AddOpossum(options =>
{
    options.RootPath = @"D:\MyData\EventStore";
    options.UseStore("MyApp");
});

// Append an event
await eventStore.AppendAsync(
    [new NewEvent { Event = new DomainEvent { Event = new OrderPlacedEvent(orderId) } }],
    condition: null);

// Read events
var events = await eventStore.ReadAsync(Query.All(), readOptions: null);
```

No Docker. No database migrations. No cloud dependencies. Just files.

---

## Why Opossum?

| Scenario | Why Opossum Fits |
|---|---|
| **Automotive / POS / field-service** | 100% offline, complete audit trail, local compliance |
| **SMB on-premises software** | No recurring cloud fees, IT staff manages files |
| **Multi-workstation LAN deployments** | Cross-process safety via OS file locking |
| **Development & testing** | Zero infrastructure — just run the app |
| **Compliance-heavy industries** | Immutable event log, optional OS-level write protection |

---

## What's Included

- ✅ **Event Store** — Append and read immutable events stored as JSON files
- ✅ **Optimistic Concurrency** — DCB `AppendCondition` prevents lost updates
- ✅ **Tag-Based Indexing** — Fast domain-scoped queries without full scans
- ✅ **Projections** — Auto-maintained read models that rebuild from events
- ✅ **Mediator** — Command/query dispatch with automatic handler discovery via DI
- ✅ **Cross-Process Safety** — OS-level file lock serialises appends across machines
- ✅ **Durability Guarantees** — Configurable flush-to-disk on every write
- ✅ **OpenTelemetry** — Built-in activity source for distributed tracing

---

## Getting Started

→ [Installation](articles/getting-started/installation.md)  
→ [Quick Start](articles/getting-started/quick-start.md)  
→ [Configuration](articles/getting-started/configuration.md)

## Learn the Concepts

→ [What is an Event Store?](articles/concepts/event-store.md)  
→ [DCB Specification](articles/concepts/dcb.md)  
→ [Projections](articles/concepts/projections.md)  
→ [Mediator Pattern](articles/concepts/mediator.md)

## Explore the API

→ [API Reference](api/Opossum.yml)
