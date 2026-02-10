# Cross-Platform Path Configuration

## Problem

Tests were failing on Ubuntu because of Windows-specific absolute paths:
- ❌ `"RootPath": "D:\\Database"` in `appsettings.json`
- ❌ Drive letters don't exist on Linux
- ❌ Validation failed: `Path.IsPathRooted("D:\\Database")` returns `false` on Linux

## Solution

### 1. appsettings.json (Base Configuration)

**Changed:**
```json
{
  "Opossum": {
    "RootPath": "",  // Empty - use platform default
    "Contexts": ["OpossumSampleApp"]
  }
}
```

**Why:** Base configuration is platform-agnostic. Defaults are applied in code.

### 2. appsettings.Development.json (Windows Local Dev)

**Unchanged:**
```json
{
  "Opossum": {
    "RootPath": "D:\\Database",  // Windows-specific path
    "Contexts": ["OpossumSampleApp"]
  }
}
```

**Why:** Local Windows development still uses `D:\Database`. Works perfectly.

### 3. Program.cs (Platform-Aware Defaults)

**Added:**
```csharp
var configuredPath = builder.Configuration["Opossum:RootPath"];

if (string.IsNullOrWhiteSpace(configuredPath))
{
    // Default to platform-appropriate path
    options.RootPath = OperatingSystem.IsWindows() 
        ? Path.Combine("D:", "Database")  // Windows: D:\Database
        : "/var/opossum/data";            // Linux: /var/opossum/data
}
else
{
    options.RootPath = configuredPath;
}
```

**Why:** When no path is configured (Ubuntu CI), uses Linux-appropriate default.

### 4. OpossumOptionsValidator.cs (Better Error Messages)

**Improved:**
```csharp
if (!Path.IsPathRooted(options.RootPath))
{
    failures.Add($"RootPath must be an absolute path: {options.RootPath}. " +
        $"Examples: Windows 'C:\\Database', Linux '/var/opossum/data'");
}
```

**Why:** Cross-platform examples in error messages.

---

## How It Works Now

### Windows Local Development (F5 in Visual Studio)

1. Loads `appsettings.json` (RootPath = "")
2. Loads `appsettings.Development.json` (RootPath = "D:\\Database") ✅
3. Uses `D:\Database` - **your seeded database**

### Ubuntu CI (GitHub Actions)

1. Loads `appsettings.json` (RootPath = "")
2. No Development.json
3. Code detects Linux → Uses `/var/opossum/data` ✅
4. Tests override with temp paths anyway ✅

### Tests (Both Platforms)

1. Fixture sets temp path: `Path.GetTempPath()/OpossumTests_<guid>`
2. Windows: `C:\Users\...\Temp\OpossumTests_abc123`
3. Linux: `/tmp/OpossumTests_abc123`
4. Both are absolute ✅

---

## Configuration Priority

From lowest to highest:
1. `appsettings.json` - Empty RootPath
2. `appsettings.{Environment}.json` - Development = "D:\\Database"
3. **Environment Variables** - `OPOSSUM__ROOTPATH=/custom/path`
4. **Code Defaults** - Platform detection if empty
5. **Test Overrides** - ConfigureServices replacement

---

## Production Deployment

### Docker (Linux)

**Option 1: Environment Variable**
```dockerfile
ENV OPOSSUM__ROOTPATH=/var/opossum/data
```

**Option 2: appsettings.Production.json**
```json
{
  "Opossum": {
    "RootPath": "/var/opossum/data"
  }
}
```

### Windows Server

**Option 1: appsettings.Production.json**
```json
{
  "Opossum": {
    "RootPath": "C:\\OpossumData"
  }
}
```

**Option 2: Environment Variable**
```powershell
$env:OPOSSUM__ROOTPATH="C:\OpossumData"
```

---

## Path.IsPathRooted() Cross-Platform Behavior

### Windows
```csharp
Path.IsPathRooted("C:\\Database")     // ✅ true
Path.IsPathRooted("D:\\Database")     // ✅ true
Path.IsPathRooted("\\\\server\\share") // ✅ true (UNC)
Path.IsPathRooted("Database")         // ❌ false
Path.IsPathRooted(".\\Database")      // ❌ false
```

### Linux
```csharp
Path.IsPathRooted("/var/data")    // ✅ true
Path.IsPathRooted("/tmp/test")    // ✅ true
Path.IsPathRooted("var/data")     // ❌ false
Path.IsPathRooted("./var/data")   // ❌ false
Path.IsPathRooted("~/data")       // ❌ false (~ not expanded)
Path.IsPathRooted("D:\\Database") // ❌ false (drive letters invalid!)
```

**Key Insight:** `D:\Database` is NOT rooted on Linux!

---

## Testing

### Verify Windows Paths
```powershell
# Should use D:\Database locally
dotnet run --project Samples\Opossum.Samples.CourseManagement\

# Check logs for RootPath
# Should show: D:\Database (from appsettings.Development.json)
```

### Verify Linux Paths (WSL or Docker)
```bash
# Should use /var/opossum/data
dotnet run --project Samples/Opossum.Samples.CourseManagement/

# Or override
export OPOSSUM__ROOTPATH=/tmp/opossum
dotnet run --project Samples/Opossum.Samples.CourseManagement/
```

### Verify Tests (Both Platforms)
```bash
dotnet test

# All tests should pass
# Temp paths used: /tmp/OpossumTests_<guid> (Linux)
#                  C:\Users\...\Temp\OpossumTests_<guid> (Windows)
```

---

## Summary

✅ **Windows Development:** Still uses `D:\Database` (seeded data)  
✅ **Linux CI/CD:** Uses `/var/opossum/data` (or temp in tests)  
✅ **Tests:** Platform-aware temp paths  
✅ **Validation:** Cross-platform path checking  
✅ **Production:** Flexible deployment via env vars or config files

**All 743 tests should now pass on both Windows and Ubuntu!** 🎉
