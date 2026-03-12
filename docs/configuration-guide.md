# Configuration Guide - Opossum Sample App

## Overview

The Opossum Sample Application uses **appsettings.json** for all configuration. This provides:
- ✅ Clear, centralized configuration
- ✅ Environment-specific overrides (Development, Testing, Production)
- ✅ Easy test isolation without code changes
- ✅ Standard .NET configuration patterns

---

## Configuration Files

### appsettings.json (Default)

Used for normal development and production:

```json
{
  "Opossum": {
    "RootPath": "D:\\Database",
    "StoreName": "OpossumSampleApp",
    "FlushEventsImmediately": true
  },

  "Projections": {
    "AutoRebuild": "MissingCheckpointsOnly",
    "MaxConcurrentRebuilds": 4,
    "PollingInterval": "00:00:01",
    "BatchSize": 50,
    "RebuildFlushInterval": 10000
  }
}
```

### appsettings.Testing.json (Test Environment)

Automatically loaded when `ASPNETCORE_ENVIRONMENT=Testing`:

```json
{
  "Projections": {
    "AutoRebuild": "None"
  }
}
```

**Why disable auto-rebuild in tests?**
- Prevents lock conflicts between daemon and test-initiated rebuilds
- Tests control when rebuilds happen
- Faster test startup (no automatic rebuilding)

---

## Configuration Options

### Opossum Section

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `RootPath` | string | `"OpossumStore"` | Root directory for event store data |
| `StoreName` | string | *(none)* | Name of the event store (subdirectory under RootPath). Set via `UseStore()` or config binding |
| `FlushEventsImmediately` | bool | `true` | Force disk flush after writes (durability) |

### Projections Section

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AutoRebuild` | `AutoRebuildMode` | `MissingCheckpointsOnly` | Controls startup rebuild behaviour: `None`, `MissingCheckpointsOnly`, or `ForceFullRebuild` |
| `MaxConcurrentRebuilds` | int | `4` | Max projections to rebuild in parallel |
| `PollingInterval` | TimeSpan | `"00:00:05"` | How often to poll for new events |
| `BatchSize` | int | `1000` | Events to process per batch |
| `RebuildBatchSize` | int | `5000` | Events to load per batch during a projection rebuild. Lower values reduce peak memory; higher values reduce I/O round-trips |
| `RebuildFlushInterval` | int | `10000` | Events processed between rebuild journal flushes. Controls the maximum re-work on crash recovery. Lower = more durable but more I/O; higher = less overhead but more re-work on recovery. Range: 100–1,000,000 |

---

## Environment-Specific Configuration

### Development (Default)

```bash
# Just run the app
dotnet run
```

Uses: `appsettings.json`
- Database: `D:\Database`
- Auto-rebuild: `MissingCheckpointsOnly`

### Testing (Integration Tests)

```bash
# Tests automatically use Testing environment
dotnet test
```

Uses: `appsettings.json` + `appsettings.Testing.json`
- Database: Unique temp folder per test run
- Auto-rebuild: `false` (from appsettings.Testing.json)

### Production

```bash
# Set environment variable
$env:ASPNETCORE_ENVIRONMENT="Production"
dotnet run
```

Create `appsettings.Production.json`:
```json
{
  "Opossum": {
    "RootPath": "/var/opossum/data"
  },
  "Projections": {
    "AutoRebuild": "None",
    "MaxConcurrentRebuilds": 8
  }
}
```

---

## Overriding Configuration

### 1. Environment Variables (Highest Priority)

```bash
# Windows PowerShell
$env:Opossum__RootPath="C:\CustomPath"
$env:Projections__AutoRebuild="None"
dotnet run

# Linux/Mac
export Opossum__RootPath="/custom/path"
export Projections__AutoRebuild="None"
dotnet run
```

**Note:** Use double underscores `__` to separate sections in environment variables.

### 2. User Secrets (Development Only)

```bash
dotnet user-secrets set "Opossum:RootPath" "C:\MyDevDatabase"
```

### 3. Command Line Arguments

```bash
dotnet run --Opossum:RootPath="C:\TempDatabase"
```

---

## Test Configuration

### How Tests Override Database Path

```csharp
// In IntegrationTestFixture.cs
builder.UseEnvironment("Testing");  // Loads appsettings.Testing.json

builder.ConfigureAppConfiguration((context, config) =>
{
    config.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Opossum:RootPath"] = _testDatabasePath  // Unique temp path
    });
});
```

**Result:**
1. `appsettings.json` loads with defaults
2. `appsettings.Testing.json` overlays (sets `AutoRebuild=None`)
3. In-memory config overlays (sets `RootPath` to temp folder)

---

## Configuration Priority (Lowest to Highest)

1. **appsettings.json** - Base configuration
2. **appsettings.{Environment}.json** - Environment overrides
3. **User Secrets** - Development secrets
4. **Environment Variables** - Deployment configuration
5. **Command Line** - Runtime overrides
6. **In-Memory (Tests)** - Test-specific overrides

---

## Troubleshooting

### Problem: Tests still using production database

**Check:**
1. `appsettings.Testing.json` exists in sample app project
2. `IntegrationTestFixture` calls `builder.UseEnvironment("Testing")`
3. Test output shows temp path: `C:\Users\...\Temp\OpossumTests_<guid>`

**Verify:**
```csharp
// Add logging in Program.cs
var config = builder.Configuration.GetSection("Opossum").Get<OpossumOptions>();
Console.WriteLine($"Using RootPath: {config.RootPath}");
```

### Problem: Configuration not loading

**Check:**
1. JSON files are marked as "Content" and "Copy if newer" in .csproj
2. JSON is valid (no trailing commas)
3. Property names match exactly (case-insensitive but spelling matters)

**Debug:**
```csharp
// Log all configuration values
foreach (var kvp in builder.Configuration.AsEnumerable())
{
    Console.WriteLine($"{kvp.Key} = {kvp.Value}");
}
```

---

## Best Practices

✅ **DO:**
- Use appsettings.json for all configuration
- Create environment-specific overrides (appsettings.Production.json)
- Use environment variables for sensitive data in production
- Document configuration changes in this file

❌ **DON'T:**
- Hardcode paths in Program.cs
- Use `Environment.IsEnvironment()` checks for configuration
- Store secrets in appsettings.json (use User Secrets or Azure Key Vault)
- Commit production connection strings to source control

---

## Migration from Old Approach

**Before (Hardcoded):**
```csharp
var rootPath = builder.Configuration["Opossum:RootPath"] ?? "D:\\Database";
options.AutoRebuild = builder.Environment.IsEnvironment("Testing") ? AutoRebuildMode.None : AutoRebuildMode.MissingCheckpointsOnly;
```

**After (Configuration-Based):**
```csharp
builder.Configuration.GetSection("Opossum").Bind(options);
builder.Configuration.GetSection("Projections").Bind(options);
```

**Benefits:**
- No environment checks in code
- Clear visibility of all configuration
- Easy to override per environment
- Standard .NET practices
