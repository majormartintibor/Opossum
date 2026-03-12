# Installation

## Prerequisites

- **.NET 10** — Opossum targets `net10.0`.
- A writable directory on a local or network drive for event storage.

## Install via NuGet

### .NET CLI

```bash
dotnet add package Opossum
```

### Package Manager Console (Visual Studio)

```powershell
Install-Package Opossum
```

### PackageReference (`.csproj`)

```xml
<PackageReference Include="Opossum" Version="*" />
```

> Check [NuGet](https://www.nuget.org/packages/Opossum/) for the latest stable version.

---

## Register with Dependency Injection

Opossum integrates with Microsoft's standard `IServiceCollection`. All services chain from `AddOpossum()`:

```csharp
using Opossum.DependencyInjection;
using Opossum.Mediator;
using Opossum.Projections;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    // Core event store
    .AddOpossum(options =>
    {
        options.RootPath = @"D:\MyAppData\EventStore"; // where events are stored
        options.UseStore("MyApp");                      // store name
        options.FlushEventsImmediately = true;          // durability (recommended for production)
    })
    // Mediator (optional — for command/query handling)
    .AddMediator()
    // Projection system (optional — for read models)
    .AddProjections(options =>
    {
        options.ScanAssembly(typeof(Program).Assembly); // auto-discover IProjectionDefinition<T>
    });

var app = builder.Build();
app.Run();
```

### What gets registered

**`AddOpossum()`**

| Service | Lifetime | Description |
|---|---|---|
| `IEventStore` | Singleton | Core append/read interface |
| `IEventStoreAdmin` | Singleton | Destructive admin operations (`DeleteStoreAsync`) |
| `IEventStoreMaintenance` | Singleton | Additive maintenance operations (retroactive tag migration) |

**`AddProjections()`**

| Service | Lifetime | Description |
|---|---|---|
| `IProjectionManager` | Singleton | Manages projection state and updates |
| `IProjectionRebuilder` | Singleton | Orchestrates projection rebuilds |
| `IProjectionStore<TState>` | Singleton | Reads projection state — one registration per projection type, created automatically during assembly scan |

**`AddMediator()`**

| Service | Lifetime | Description |
|---|---|---|
| `IMediator` | Singleton | Message dispatch |

---

## Minimum Configuration

The only required configuration is a `RootPath` and a store name:

```csharp
builder.Services.AddOpossum(options =>
{
    options.RootPath = @"D:\MyData";
    options.UseStore("MyApp");
});
```

Opossum creates the directory structure automatically on first startup.

---

## Next Steps

→ [Quick Start](quick-start.md) — build your first event-sourced feature in 5 minutes  
→ [Configuration](configuration.md) — full reference for all options
