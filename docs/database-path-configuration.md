# Database Path Configuration

## How It Works

The sample application's database path is configured to support both manual operation and automated testing:

```csharp
// In Program.cs
var rootPath = builder.Configuration["Opossum:RootPath"] ?? "D:\\Database";
```

### Scenarios

| Scenario | Configuration Override | Resulting Path |
|----------|----------------------|----------------|
| **Manual Start** (F5, dotnet run) | None | `D:\Database` |
| **Integration Tests** | `IntegrationTestFixture` sets override | `C:\Users\...\Temp\OpossumTests_<guid>` |
| **Custom Override** | Set `OPOSSUM__ROOTPATH` env variable | Custom path |

### Manual Operation (Production/Development)

When you start the app manually:
- No configuration override exists
- Falls back to default: `"D:\\Database"`
- Uses your manually seeded database ✅

**To start manually:**
```bash
# Visual Studio
F5

# Command line
cd Samples\Opossum.Samples.CourseManagement
dotnet run
```

**Database location:** `D:\Database\OpossumSampleApp\`

### Test Execution (Automated)

When tests run:
- `IntegrationTestFixture` provides configuration override
- Each test class gets unique temp path: `C:\Users\...\Temp\OpossumTests_<guid>`
- Production database never touched ✅

**To run tests:**
```bash
dotnet test tests\Samples\Opossum.Samples.CourseManagement.IntegrationTests\
```

**Database location:** Temporary folders (auto-cleaned)

### Custom Override (Optional)

You can override via environment variable:
```bash
# Windows
set OPOSSUM__ROOTPATH=C:\MyCustomPath
dotnet run

# PowerShell
$env:OPOSSUM__ROOTPATH="C:\MyCustomPath"
dotnet run

# Linux/Mac
export OPOSSUM__ROOTPATH=/custom/path
dotnet run
```

## Verification

Run diagnostic tests to verify isolation:
```bash
dotnet test tests\Samples\Opossum.Samples.CourseManagement.IntegrationTests\ --filter "FullyQualifiedName~DiagnosticTests"
```

These tests verify:
1. Production database (`D:\Database`) is NOT modified during tests
2. Tests start with empty databases (all checkpoints = 0)

## Troubleshooting

**Problem:** Tests seem slow or are modifying production database

**Solution:** Check that:
1. `Program.cs` reads from configuration: `builder.Configuration["Opossum:RootPath"]`
2. `IntegrationTestFixture` provides override via `ConfigureAppConfiguration`
3. No hardcoded paths in `Program.cs`

**Verify with:**
```csharp
// Add temporary logging in Program.cs
Console.WriteLine($"Using database path: {rootPath}");
```

You should see different paths for manual vs test execution.
