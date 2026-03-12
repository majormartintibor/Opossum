# Installation

## Prerequisites

- **.NET 8 or later** — Opossum targets `net8.0` and above. .NET 10 is recommended.
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

Opossum integrates with Microsoft's standard `IServiceCollection`:

```csharp
using Opossum.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Core event store
builder.Services.AddOpossum(options =>
{
    options.RootPath = @"D:\MyAppData\EventStore"; // where events are stored
    options.UseStore("MyApp");                      // store/context name
    options.FlushEventsImmediately = true;          // durability (recommended for production)
});

// Projection system (optional — for read models)
builder.Services.AddProjections(options =>
{
    options.ScanAssembly(typeof(Program).Assembly); // auto-discover IProjectionDefinition<T>
});

// Mediator (optional — for command/query handling)
builder.Services.AddMediator();

var app = builder.Build();
app.Run();
```

### What gets registered

| Service | Lifetime | Description |
|---|---|---|
| `IEventStore` | Singleton | Core append/read interface |
| `IEventStoreAdmin` | Singleton | Administrative operations (tag migration, etc.) |
| `IEventStoreMaintenance` | Singleton | Maintenance operations (add tags retroactively) |
| `IProjectionManager` | Singleton | Manages projection state and updates |
| `IProjectionRebuilder` | Singleton | Orchestrates projection rebuilds |
| `IProjectionStore` | Singleton | Reads projection state |
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
