# ConfigureAwait(false) Implementation Guide

## Status

**Phase 1: Critical Files** - ✅ COMPLETED
- EventFileManager.cs - ✅ Done
- FileSystemProjectionStore.cs - ✅ Done  
- FileSystemEventStore.cs - ✅ Done

**Phase 2: Remaining Library Files** - 🔄 IN PROGRESS

## Files Requiring ConfigureAwait(false)

Based on code analysis, the following files still need `ConfigureAwait(false)` added:

### Storage Layer
1. ✅ `src/Opossum/Storage/FileSystem/EventFileManager.cs` - DONE
2. ✅ `src/Opossum/Storage/FileSystem/FileSystemEventStore.cs` - DONE
3. ⏳ `src/Opossum/Storage/FileSystem/LedgerManager.cs`
4. ⏳ `src/Opossum/Storage/FileSystem/TagIndex.cs`
5. ⏳ `src/Opossum/Storage/FileSystem/EventTypeIndex.cs`
6. ⏳ `src/Opossum/Storage/FileSystem/IndexManager.cs`

### Projection Layer
7. ✅ `src/Opossum/Projections/FileSystemProjectionStore.cs` - DONE
8. ⏳ `src/Opossum/Projections/ProjectionManager.cs` - PARTIALLY DONE (2 awaits done, more remaining)
9. ⏳ `src/Opossum/Projections/ProjectionDaemon.cs`
10. ⏳ `src/Opossum/Projections/ProjectionTagIndex.cs`
11. ⏳ `src/Opossum/Projections/ProjectionMetadataIndex.cs`

### Mediator
12. ⏳ `src/Opossum/Mediator/Mediator.cs`
13. ⏳ `src/Opossum/Mediator/ReflectionMessageHandler.cs`

## Manual Fix Instructions

For each file, find all `await` statements and add `.ConfigureAwait(false)` before the semicolon or closing parenthesis.

### Pattern to Find
```regex
await\s+(.+?)(\))?;
```

### Replace With
```csharp
await $1.ConfigureAwait(false);
```

### Example Transformations

**Before:**
```csharp
var json = await File.ReadAllTextAsync(filePath);
var data = await reader.ReadToEndAsync();
await _lock.WaitAsync(cancellationToken);
```

**After:**
```csharp
var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
var data = await reader.ReadToEndAsync().ConfigureAwait(false);
await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
```

## Automated PowerShell Script

**⚠️ IMPORTANT: Close Visual Studio and any editors before running this script!**

Save as `Add-ConfigureAwait.ps1`:

```powershell
# Add-ConfigureAwait.ps1
# Adds ConfigureAwait(false) to all await statements in Opossum library code

$files = @(
    'src\Opossum\Storage\FileSystem\LedgerManager.cs',
    'src\Opossum\Storage\FileSystem\TagIndex.cs',
    'src\Opossum\Storage\FileSystem\EventTypeIndex.cs',
    'src\Opossum\Storage\FileSystem\IndexManager.cs',
    'src\Opossum\Projections\ProjectionManager.cs',
    'src\Opossum\Projections\ProjectionDaemon.cs',
    'src\Opossum\Projections\ProjectionTagIndex.cs',
    'src\Opossum\Projections\ProjectionMetadataIndex.cs',
    'src\Opossum\Mediator\Mediator.cs',
    'src\Opossum\Mediator\ReflectionMessageHandler.cs'
)

foreach ($file in $files) {
    if (Test-Path $file) {
        Write-Host "Processing $file..." -ForegroundColor Cyan
        
        $content = Get-Content $file -Raw -Encoding UTF8
        $original = $content
        
        # Pattern 1: await SomeMethod() -> await SomeMethod().ConfigureAwait(false)
        # But don't double-add if already has ConfigureAwait
        $content = $content -replace 'await\s+([^\r\n;]+?)(\))(\s*;)', 'await $1).ConfigureAwait(false)$3'
        
        # Pattern 2: await SomeMethod -> await SomeMethod.ConfigureAwait(false)
        $content = $content -replace 'await\s+([^\r\n;(]+?)(\s*;)', 'await $1.ConfigureAwait(false)$2'
        
        # Fix double ConfigureAwait
        $content = $content -replace '\.ConfigureAwait\(false\)\.ConfigureAwait\(false\)', '.ConfigureAwait(false)'
        
        if ($content -ne $original) {
            Set-Content -Path $file -Value $content -NoNewline -Encoding UTF8
            Write-Host "  ✅ Updated $file" -ForegroundColor Green
        } else {
            Write-Host "  ⏭️  No changes needed for $file" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  ❌ File not found: $file" -ForegroundColor Red
    }
}

Write-Host "`n✅ ConfigureAwait(false) addition complete!" -ForegroundColor Green
Write-Host "Run 'dotnet build' to verify no compilation errors." -ForegroundColor Cyan
```

### Usage

```powershell
# Close all editors first!
# Run from repository root
.\Add-ConfigureAwait.ps1
```

## Manual Verification Checklist

After running the script, manually verify these critical paths:

### 1. LedgerManager.cs
- [ ] `GetNextSequencePositionAsync()` - File.ReadAllTextAsync
- [ ] `UpdateSequencePositionAsync()` - File.WriteAllTextAsync
- [ ] `GetLastSequencePositionAsync()` - File.ReadAllTextAsync

### 2. TagIndex.cs
- [ ] `GetPositionsByTagAsync()` - File.ReadAllTextAsync
- [ ] `AddEventAsync()` - File.WriteAllTextAsync
- [ ] All file I/O operations

### 3. EventTypeIndex.cs
- [ ] `GetPositionsByEventTypeAsync()` - File.ReadAllTextAsync
- [ ] `AddEventAsync()` - File.WriteAllTextAsync
- [ ] All file I/O operations

### 4. IndexManager.cs
- [ ] All await calls to TagIndex and EventTypeIndex methods

### 5. ProjectionManager.cs
- [ ] `RebuildAsync()` - eventStore.ReadAsync, registration.ApplyAsync
- [ ] `UpdateAsync()` - registration.ApplyAsync
- [ ] `GetCheckpointAsync()` - File.ReadAllTextAsync
- [ ] `SaveCheckpointAsync()` - File.WriteAllTextAsync

### 6. ProjectionDaemon.cs
- [ ] `ExecuteAsync()` - Task.Delay, RebuildMissingProjectionsAsync, ProcessNewEventsAsync
- [ ] `RebuildMissingProjectionsAsync()` - All projection operations
- [ ] `ProcessNewEventsAsync()` - eventStore.ReadAsync, projectionManager.UpdateAsync

### 7. ProjectionTagIndex.cs
- [ ] All File.ReadAllTextAsync calls
- [ ] All File.WriteAllTextAsync calls

### 8. ProjectionMetadataIndex.cs
- [ ] `GetAsync()` - File.ReadAllTextAsync
- [ ] `SaveAsync()` - File.WriteAllTextAsync
- [ ] `DeleteAsync()` - File operations

### 9. Mediator.cs
- [ ] `InvokeAsync()` - handler.HandleAsync

### 10. ReflectionMessageHandler.cs
- [ ] `HandleAsync()` - Task await

## Testing After Changes

```bash
# 1. Build
dotnet build

# 2. Run all tests
dotnet test tests/Opossum.UnitTests/
dotnet test tests/Opossum.IntegrationTests/

# 3. Verify no behavioral changes
# All tests should pass exactly as before
```

## Common Pitfalls to Avoid

### ❌ DON'T add to already-added ConfigureAwait
```csharp
// Wrong - double ConfigureAwait
await File.ReadAllTextAsync(file).ConfigureAwait(false).ConfigureAwait(false);
```

### ❌ DON'T add to non-library code
```csharp
// Samples/ and tests/ should NOT have ConfigureAwait(false)
// These are application code, not library code
```

### ✅ DO add to ALL library awaits
```csharp
// Correct - all awaits in src/Opossum/
await someTask.ConfigureAwait(false);
await File.ReadAllTextAsync(path).ConfigureAwait(false);
await _lock.WaitAsync(ct).ConfigureAwait(false);
```

## .editorconfig Rule

Once all files are updated, add this to `.editorconfig`:

```ini
# .editorconfig additions for ConfigureAwait

[src/Opossum/**/*.cs]
# CA2007: Do not directly await a Task in library code
dotnet_diagnostic.CA2007.severity = error

# VSTHRD111: Use ConfigureAwait(false) in library code  
dotnet_diagnostic.VSTHRD111.severity = error
```

## Copilot Instructions Update

Add to `.github/copilot-instructions.md`:

```markdown
## Async/Await in Library Code

**ALWAYS use `ConfigureAwait(false)` for all `await` statements in library code (`src/Opossum/`).**

✅ Correct:
```csharp
var data = await File.ReadAllTextAsync(path).ConfigureAwait(false);
await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
```

❌ Wrong:
```csharp
var data = await File.ReadAllTextAsync(path); // Missing ConfigureAwait(false)
```

**DO NOT use `ConfigureAwait(false)` in application code:**
- ❌ `Samples/**/*.cs` - Application code
- ❌ `tests/**/*.cs` - Test code

This prevents deadlocks when library is used in UI applications (WPF, WinForms, Blazor).
```

## Progress Tracking

| File | Status | Notes |
|------|--------|-------|
| EventFileManager.cs | ✅ | Completed manually |
| FileSystemProjectionStore.cs | ✅ | Completed manually |
| FileSystemEventStore.cs | ✅ | Completed manually |
| LedgerManager.cs | ⏳ | Pending |
| TagIndex.cs | ⏳ | Pending |
| EventTypeIndex.cs | ⏳ | Pending |
| IndexManager.cs | ⏳ | Pending |
| ProjectionManager.cs | 🔄 | Partially done |
| ProjectionDaemon.cs | ⏳ | Pending |
| ProjectionTagIndex.cs | ⏳ | Pending |
| ProjectionMetadataIndex.cs | ⏳ | Pending |
| Mediator.cs | ⏳ | Pending |
| ReflectionMessageHandler.cs | ⏳ | Pending |

## Completion Criteria

- [ ] All library files have ConfigureAwait(false)
- [ ] Build succeeds with no warnings
- [ ] All 609 tests pass (512 unit + 97 integration)
- [ ] No behavioral changes detected
- [ ] Analyzer rules configured in .editorconfig
- [ ] copilot-instructions.md updated with rule

---

**Last Updated:** 2025-01-28  
**Status:** 30% Complete (3 of 13 critical files done)  
**Next Step:** Close VS, run PowerShell script to complete remaining files
