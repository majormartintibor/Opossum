# Copilot Instructions

## What is Opossum?
Opossum is a .NET library that turns your file system into an event store database.
It also provides features like projections, mediator pattern, and dependency injection integration.
It follows the DCB (Dynamic Consistency Boundaries) specification for event sourcing.

## Glossary
- **Event Store**: A database that stores events as the primary source of truth.
- **Projection**: A read model derived from events in the event store.
- **DCB (Dynamic Consistency Boundaries)**: read D:\Codeing\FileSystemEventStoreWithDCB\Opossum\Specification\DCB-Specification.md
- **Durability**: Guarantee that persisted events survive power failures (see docs/implementation/durability-guarantees.md)

## Language and Framework

Use .NET 10 and C# 14
Prefer using the newest language features

## Code Style — .editorconfig is the Source of Truth

The repository root contains an `.editorconfig` that is the single source of truth for all code style rules.
Always follow it when generating or modifying code. Key rules to apply actively during code generation:

### Collection expressions (C# 12+)
```csharp
// ✅
int[] x = [1, 2, 3];
List<string> names = [];
// ❌
int[] x = new int[] { 1, 2, 3 };
List<string> names = new List<string>();
```

### Target-typed new (C# 9+)
```csharp
// ✅
private readonly SemaphoreSlim _lock = new(1, 1);
// ❌
private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
```

### Pattern matching (C# 8+)
```csharp
// ✅
if (exception is AppendConditionFailedException ex) { }
if (value is not null) { }
// ❌
if (exception is AppendConditionFailedException) { var ex = (AppendConditionFailedException)exception; }
if (value != null) { }
```

### Switch expressions (C# 8+)
```csharp
// ✅
var result = status switch { Status.Ok => "ok", _ => "fail" };
// ❌
string result; switch (status) { case Status.Ok: result = "ok"; break; ... }
```

### Null operators
```csharp
// ✅
_field ??= new();
value?.Method();
_field = arg ?? throw new ArgumentNullException(nameof(arg));
// ❌
if (_field == null) _field = new();
if (value != null) value.Method();
```

### Index & range operators (C# 8+)
```csharp
// ✅
var last = arr[^1];
var slice = arr[1..^1];
// ❌
var last = arr[arr.Length - 1];
```

## Documentation

All documentation files (*.md) must be placed in the `docs` folder at the solution root.
This includes:
- Architecture documentation
- Feature specifications
- Refactoring summaries
- Design decisions
- API documentation

Never create .md files in the solution root or scattered across project folders.

### Solution File (.slnx) Synchronization

The `Opossum.slnx` file MUST always be kept in sync with the actual `docs` folder structure and content.

**When adding/removing documentation:**
1. ✅ Add or remove the file in the docs folder
2. ✅ Update the corresponding entry in `Opossum.slnx`
3. ✅ Maintain the correct folder hierarchy in the .slnx file
4. ✅ Keep files sorted alphabetically within each folder section

**Example:**
If you add `docs/performance/new-benchmark.md`, you must also add:
```xml
<Folder Name="/docs/performance/">
  <File Path="docs/performance/PERFORMANCE-BASELINE.md" />
  <File Path="docs/performance/new-benchmark.md" />
</Folder>
```

This ensures the documentation is visible and navigable in Visual Studio's Solution Explorer.

### GitHub Pages Documentation Site

The documentation site lives under `docs/docfx/` and is published automatically to
`https://majormartintibor.github.io/Opossum/` on every push to `master`.

**Every code change that affects a public API, behaviour, or configuration option MUST also update the relevant article(s) in `docs/docfx/articles/`.**

#### What to update and where

| Change type | Articles to update |
|---|---|
| New public type, interface, or method | `getting-started/installation.md` (registered-services table if applicable) + any article with a relevant code example |
| Changed method signature or return type | Every article that contains a code example calling that method |
| New or changed configuration option | `getting-started/configuration.md`, `getting-started/installation.md` |
| New feature or concept | Author or extend the relevant article in the appropriate section (`concepts/`, `guides/`) |
| Renamed type or namespace | All articles that reference the old name |

#### Copied articles

Articles marked with a `<!-- source: docs/... — keep in sync -->` comment at the top are
copies of files from `docs/`. When the source file changes, update the copy in
`docs/docfx/articles/` too.

| Source | Copy in `docs/docfx/articles/` |
|---|---|
| `docs/configuration-guide.md` | `getting-started/configuration.md` |
| `docs/specifications/DCB-Specification.md` | `concepts/dcb.md` |
| `docs/specifications/dcb-projections.md` | `concepts/projections.md` |
| `docs/guides/use-cases.md` | `guides/use-cases.md` |
| `docs/guides/durability-quick-reference.md` | `guides/durability.md` |
| `docs/configuration-validation.md` | `guides/configuration-validation.md` |
| `docs/decisions/001-*.md` | `decisions/adr-001.md` |
| `docs/decisions/002-*.md` | `decisions/adr-002.md` |
| `docs/decisions/003-*.md` | `decisions/adr-003.md` |

#### Local verification before committing

Always run DocFX locally after editing articles and confirm zero warnings:

```bash
docfx docs/docfx/docfx.json
# Expected: Build succeeded.  0 warning(s)  0 error(s)
```

Install DocFX if not already present:

```bash
dotnet tool install -g docfx
```

## External Libraries
In the core Opossum project and its test projects, avoid using external libraries.
Only official Microsoft packages are allowed.
In the Sample Application project, you may suggest external libraries but avoid adding them unless absolutely necessary 
and you must explicitly ask me for permission.

## Rules for Using Statements in Opossum Project

### Core Principle

Using statements must be organized based on whether they are internal to the Opossum project or external dependencies.

### Rules

1. **Opossum Namespace Usings - STAY in .cs files**
   - ✅ Any using statement starting with `Opossum.*` MUST remain in individual `.cs` files
   - These are internal project references and should be visible in each file for clarity
   - Examples:
     - `using Opossum.Configuration;`
     - `using Opossum.Storage;`
     - `using Opossum.DependencyInjection;`

2. **External Usings - MOVE to GlobalUsings.cs**
   - ❌ External using statements should NOT appear in individual `.cs` files
   - ✅ All external using statements MUST be in `GlobalUsings.cs`
   - Examples of external usings:
     - `using System.*;`
     - `using Microsoft.*;`
     - `using Newtonsoft.*;`
     - Any third-party package namespace

3. **ImplicitUsings**
   - The project has `<ImplicitUsings>enable</ImplicitUsings>` in the `.csproj`
   - Common System namespaces are automatically included
   - Only add explicit global usings for frequently used external packages

### File Organization

**Individual .cs files should look like:**
```csharp
using Opossum.Configuration;
using Opossum.Storage;

namespace Opossum.DependencyInjection;

public class MyClass
{
    // Implementation
}
```

**GlobalUsings.cs should contain:**
```csharp
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Options;
global using Microsoft.Extensions.Logging;
// ... other commonly used external namespaces
```

### When Adding New Using Statements

1. **Is it an Opossum.* namespace?**
   - YES → Add it to the specific .cs file that needs it
   - NO → Add it to GlobalUsings.cs with `global using` prefix

2. **For existing code cleanup:**
   - Scan the file for using statements
   - Move all non-Opossum usings to GlobalUsings.cs
   - Keep all Opossum.* usings in the file

### Benefits

- ✅ Clear visibility of internal project dependencies in each file
- ✅ Reduced boilerplate for external dependencies
- ✅ Easier refactoring of internal namespaces
- ✅ Consistent code style across the project

### Example

**Before (Incorrect):**
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Opossum.Configuration;

namespace Opossum.DependencyInjection;
```

**After (Correct):**

In the .cs file:
```csharp
using Opossum.Configuration;

namespace Opossum.DependencyInjection;
```

In GlobalUsings.cs:
```csharp
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Options;
```

### Cleanup
Clean up unused using statements. No using statements should remain in individual files if they are not used.

## Central Package Management (CPM)

This repository uses **Central Package Management** for all NuGet packages.

### Rules

1. **NEVER add package versions directly to `.csproj` files**
   - ❌ Wrong: `<PackageReference Include="PackageName" Version="1.0.0" />`
   - ✅ Correct: `<PackageReference Include="PackageName" />`

2. **ALWAYS add new package versions to `Directory.Packages.props`**
   - All package versions MUST be defined in the root `Directory.Packages.props` file
   - Use `<PackageVersion Include="PackageName" Version="x.x.x" />` syntax

3. **When adding a new NuGet package:**
   - Step 1: Add the version to `Directory.Packages.props` in the appropriate category
   - Step 2: Add the package reference WITHOUT version to the project file
   - Step 3: Maintain alphabetical ordering within categories when possible

4. **Package Version Organization:**
   - Group packages by category (e.g., Microsoft Extensions, Testing, ASP.NET Core)
   - Add comments to separate different categories
   - Keep related packages together

### Example

When adding a new package like `Newtonsoft.Json`:

**In Directory.Packages.props:**
```xml
<ItemGroup>
  <!-- JSON Serialization -->
  <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
</ItemGroup>
```

**In YourProject.csproj:**
```xml
<ItemGroup>
  <PackageReference Include="Newtonsoft.Json" />
</ItemGroup>
```

### Benefits

- ✅ Single source of truth for all package versions
- ✅ Easier to update packages across the entire solution
- ✅ Prevents version conflicts between projects
- ✅ Follows .NET modern best practices
- ✅ Simplifies dependency management

### References

- [Central Package Management documentation](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management)

## Namespace Declaration
Use file-scoped namespaces:
✅ `namespace Opossum.DependencyInjection;`
❌ `namespace Opossum.DependencyInjection { }`

## Async/Await Best Practices for Library Code

**Opossum is a library (will be distributed as NuGet package), not an application.**

### CRITICAL RULE: Always Use ConfigureAwait(false)

**ALL `await` statements in library code (`src/Opossum/`) MUST use `.ConfigureAwait(false)`.**

✅ **Correct:**
```csharp
var data = await File.ReadAllTextAsync(path).ConfigureAwait(false);
await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
var events = await _eventStore.ReadAsync(query).ConfigureAwait(false);
```

❌ **Wrong:**
```csharp
var data = await File.ReadAllTextAsync(path); // ❌ Missing ConfigureAwait(false)
await _lock.WaitAsync(cancellationToken);     // ❌ Missing ConfigureAwait(false)
```

### Why This Matters

1. **Prevents Deadlocks:** When Opossum is consumed by UI applications (WPF, WinForms, Blazor), missing ConfigureAwait(false) can cause deadlocks
2. **Better Performance:** Avoids unnecessary context marshaling (~10% performance gain when sync context exists)
3. **Industry Standard:** Microsoft's official best practice for library code

### Where to Apply

✅ **DO use ConfigureAwait(false) in:**
- `src/Opossum/**/*.cs` - All library code

❌ **DO NOT use ConfigureAwait(false) in:**
- `Samples/**/*.cs` - Application code (needs context for HTTP requests, etc.)
- `tests/**/*.cs` - Test code (doesn't matter)

### Analyzer Enforcement

The project uses `Microsoft.VisualStudio.Threading.Analyzers` to enforce this rule.

If you see warning **VSTHRD111**, add `.ConfigureAwait(false)` to the await statement.

### References

- [Microsoft: ConfigureAwait FAQ](https://devblogs.microsoft.com/dotnet/configureawait-faq/)
- [David Fowler: Async Guidance](https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/master/AsyncGuidance.md)

## Compiler
All code you submit must compile without errors.

## Code Quality Standards

### Zero Warnings Policy

**All code changes MUST result in 0 compiler warnings before committing.**

#### Rules

1. **Before claiming code is complete:**
   - ✅ Run `dotnet build` and verify `0 Warning(s)` in output
   - ✅ Fix ALL warnings, including:
     - Nullable reference warnings (CS8600-CS8625)
     - Deprecated API warnings (ASPDEPR*)
     - Analyzer warnings (xUnit*, VSTHRD*, etc.)
     - Unused variable warnings (CS0219)

2. **Acceptable warning fixes:**
   - ✅ Add null checks or null-forgiving operators (`!`) where appropriate
   - ✅ Replace deprecated APIs with modern alternatives
   - ✅ Remove unused variables
   - ✅ Fix xUnit assertions on value types (use `Assert.NotEqual(default, value)`)
   - ✅ Add `ConfigureAwait(false)` for library code

3. **Warning verification command:**
   ```bash
   dotnet clean
   dotnet build 2>&1 | Select-String -Pattern "Warning\(s\)|warning"
   ```

   Expected output: `0 Warning(s)`

4. **Common warning fixes:**
   - **xUnit2002**: Don't use `Assert.NotNull()` on value types → Use `Assert.NotEqual(default, value)`
   - **ASPDEPR002**: Don't use deprecated `WithOpenApi` → Use `WithSummary()` and `WithDescription()`
   - **CS8602/CS8604**: Possible null reference → Add null check or `!` operator
   - **VSTHRD111**: Missing ConfigureAwait → Add `.ConfigureAwait(false)` in library code

#### Benefits

- ✅ Higher code quality
- ✅ Prevents warnings from accumulating
- ✅ Catches potential bugs early
- ✅ Professional codebase standards
- ✅ Easier to spot new issues

**NEVER commit code with warnings. If you cannot fix a warning, ask for guidance.**

## Testing

### Framework
Use xUnit for all tests.

### Unit Tests
Unit Test project may only contain tests that work on pure data without any external dependencies.
No mocking is allowed in Unit Tests.

### Integration Tests
Integration Test project may contain tests that depend on external dependencies like databases, file systems, etc.
Since we do not use any external dependency other than the file system, mocking is not allowed.

### Test isolation
All tests must be isolated and independent of each other.
No test must depend on the result or side effects of another test.
If needed, introduce TestCollection to group tests that share setup/teardown logic.
Since we work with the filesystem, make sure to use temporary files and folders that are cleaned up after the test run.
It is best if each test class or test collection works with its own temporary folder to avoid conflicts.
Never use the path specified in the configuration directly, always copy needed files to a temporary folder first.
example:
```csharp
builder.Services.AddOpossum(options =>
{
    options.RootPath = "D:\\Database";
    options.UseStore("OpossumSampleApp");
});
```
Never use "D:\\Database" directly in tests.

### New features
When adding new features make sure to add sufficient unit and integration tests to cover the new functionality.
All tests must pass before committing new code.

## CHANGELOG Maintenance

**MANDATORY: CHANGELOG.md MUST be updated for every code change.**

### When to Update CHANGELOG.md

Update `CHANGELOG.md` for:
- ✅ **New features** - Any new public API, functionality, or capability
- ✅ **Bug fixes** - Any bug fix, even minor ones
- ✅ **Breaking changes** - Changes that break backward compatibility
- ✅ **Performance improvements** - Significant performance optimizations
- ✅ **Documentation updates** - Major documentation additions (not typo fixes)
- ✅ **Deprecations** - Marking APIs or features as deprecated
- ⚠️ **Internal refactoring** - Only if it affects users or performance

### CHANGELOG Format

Follow [Keep a Changelog](https://keepachangelog.com/) format:

```markdown
## [Unreleased]

### Added
- New `IEventStore.QueryAsync()` method for complex queries
- Support for multiple contexts in event store

### Changed
- Improved performance of projection rebuilds by 50%

### Fixed
- Fixed race condition in concurrent event appends
- Corrected event ordering in cross-stream queries

### Deprecated
- `IEventStore.Read()` is deprecated, use `IEventStore.ReadAsync()` instead

### Removed
- Removed obsolete `EventStoreOptions.UseLegacyFormat` option

### Security
- Fixed potential file path traversal vulnerability
```

### Example Workflow

When adding a new feature:

1. Write code
2. Write tests
3. **Update CHANGELOG.md** under `## [Unreleased]` section:
   ```markdown
   ### Added
   - Multi-context support for isolating event streams (#42)
   ```
4. Commit with message: `feat: Add multi-context support`

### Why This Matters

- ✅ **Release preparation** - CHANGELOG becomes release notes
- ✅ **User communication** - Users know what changed
- ✅ **Version planning** - Helps decide MAJOR/MINOR/PATCH versions
- ✅ **History tracking** - Easy to see what changed when
- ✅ **NuGet releases** - CHANGELOG content goes to GitHub releases

**NEVER claim a feature is complete without updating CHANGELOG.md!**

## Definition of Done

A feature or task is ONLY considered complete when ALL of the following criteria are met:

### 1. Code Quality
- ✅ All code compiles successfully (`dotnet build`)
- ✅ **Zero compiler warnings** (`0 Warning(s)` in build output)
- ✅ Follows all copilot-instructions rules (namespaces, usings, etc.)
- ✅ Code is properly documented with XML comments where appropriate

### 2. Testing - MANDATORY
- ✅ **ALL existing tests pass** - Run full test suite:
  ```bash
  dotnet test tests/Opossum.UnitTests/Opossum.UnitTests.csproj
  dotnet test tests/Opossum.IntegrationTests/Opossum.IntegrationTests.csproj
  ```
- ✅ New unit tests added for new functionality
- ✅ New integration tests added for new features
- ✅ Test coverage is sufficient (edge cases, error conditions, happy paths)
- ✅ All new tests pass

### 3. Documentation
- ✅ All .md files placed in `docs` folder
- ✅ Feature documentation written (if applicable)
- ✅ Code comments updated (if needed)
- ✅ README updated (if needed)
- ✅ **CHANGELOG.md updated** (MANDATORY for all new features, bug fixes, and breaking changes)
- ✅ **GitHub Pages articles updated** — all affected `docs/docfx/articles/` pages reflect the change
- ✅ **DocFX build clean** — `docfx docs/docfx/docfx.json` reports `0 warning(s)  0 error(s)`

### 4. Verification Checklist

Before claiming "Implementation Complete", you MUST explicitly confirm:

```markdown
## Pre-Completion Verification

- [ ] ✅ Build successful: `dotnet build`
- [ ] ✅ Zero warnings: `0 Warning(s)` in build output
- [ ] ✅ Unit tests passing: `dotnet test tests/Opossum.UnitTests/`
- [ ] ✅ Integration tests passing: `dotnet test tests/Opossum.IntegrationTests/`
- [ ] ✅ Sample app runs without errors (if applicable)
- [ ] ✅ No breaking changes to existing APIs
- [ ] ✅ All documentation updated
- [ ] ✅ GitHub Pages articles updated (affected `docs/docfx/articles/` pages)
- [ ] ✅ DocFX build clean: `docfx docs/docfx/docfx.json` → `0 warning(s)  0 error(s)`
- [ ] ✅ CHANGELOG.md updated with changes
- [ ] ✅ Copilot-instructions followed
```

### 5. Breaking Changes

If you introduce breaking changes:
- ✅ Explicitly document them
- ✅ Update all affected tests
- ✅ Update sample applications
- ✅ Get explicit approval from the user BEFORE implementation

**NEVER claim a feature is complete without running the full test suite and confirming all tests pass.**

## File Encoding — UTF-8 Preservation

### CRITICAL RULE: Never Corrupt File Encoding

All Markdown and source files in this repository are **UTF-8 encoded**. Special characters (emojis like ✅ ❌ ⭐ ⚠️, em-dashes —, arrows →) MUST be preserved exactly.

### Rules

1. **Never use unicode escape sequences (`\u00e2`, `\u015b`, etc.) when editing Markdown files.**
   These represent raw UTF-8 byte values and will cause double-encoding corruption (e.g. ✅ becomes `âś…`).

2. **When editing files that contain emojis or special Unicode characters:**
   - ✅ Use `replace_string_in_file` with the literal characters copied from the file
   - ✅ If the tool cannot match special characters, use PowerShell with explicit UTF-8:
     ```powershell
     $content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
     $content = $content.Replace("old text", "new text")
     [System.IO.File]::WriteAllText($path, $content, [System.Text.Encoding]::UTF8)
     ```
   - ❌ Never pass unicode escape sequences in `oldString` or `newString` parameters

3. **After editing any Markdown file with special characters, verify encoding:**
   ```powershell
   [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8).Contains("✅")
   ```

4. **Known corruption signatures to watch for:**
   - `âś…` instead of `✅`
   - `âťŚ` instead of `❌`
   - `â€"` instead of `—`
   - `â†'` instead of `→`
   - Any `Ã` or `Â` characters appearing in Markdown prose

   If you see these, the file has been double-encoded. Restore from git and re-apply changes using the PowerShell method above.

## Documentation Accuracy — Source Code Is the Single Source of Truth

### CRITICAL RULE: Every Code Example Must Match the Actual Codebase

Documentation examples (README, docfx articles, guide files) MUST accurately reflect the real types, signatures, and patterns in `src/Opossum/`. **Never fabricate, simplify, or approximate an API.**

### Before Writing Any Code Example

1. **Look up the actual interface/class** using `get_symbols_by_name` or `find_symbol` before writing any code example that references it.
2. **Verify all members** — if showing an `IProjectionDefinition<T>` implementation, include ALL required members (`ProjectionName`, `EventTypes`, `KeySelector`, `Apply`), not just `Apply`.
3. **Verify method signatures** — check the actual parameter list (names, types, defaults). Do not add or remove parameters (e.g. `AppendCondition? condition = null` when the actual has no default, or omitting `maxCount` from `ReadAsync`).
4. **Verify types exist** — do not reference `IProjectionStore` (non-generic) if only `IProjectionStore<TState>` exists. Do not reference `IMessageHandler<TMessage, TResponse>` if only `IMessageHandler` (non-generic) exists.

### Directory and File Structure in Documentation

When documenting the on-disk storage layout:
- ✅ Check `StorageInitializer.cs` for the actual directory names (`Events/`, `Indices/`, `Projections/`, `.ledger`)
- ✅ Check `EventFileManager.cs` for the actual file naming pattern (`{position:D10}.json` → `0000000001.json`)
- ❌ Never guess directory names or file patterns — they are **case-sensitive** on some OS and must match the code exactly

### Cross-Document Consistency

**README.md and docfx articles MUST NOT contradict each other on how something works.**

They may differ in:
- ✅ Level of detail (README can be briefer)
- ✅ Which features they showcase
- ✅ Prose style and framing

They must NOT differ in:
- ❌ API signatures, type names, or method parameters
- ❌ Required interface members shown in examples
- ❌ On-disk file/directory names and structure
- ❌ Business logic in code examples (e.g. which invariant checks a handler performs)

**Verification step:** When changing a code example in any documentation file, search all other documentation files for the same type/method/pattern and ensure they are consistent:
```powershell
Select-String -Path "README.md","docs/docfx/articles/**/*.md" -Pattern "TheTypeOrMethodName" -Recurse
```

### Keeping Documentation Copies in Sync

Files under `docs/docfx/articles/` that have a `<!-- source: docs/... — keep in sync -->` header are copies. When editing:

1. **Always edit the source file first** (under `docs/`)
2. **Then propagate to the docfx copy** using the UTF-8-safe PowerShell copy method:
   ```powershell
   $header = "<!-- source: docs/path/to/source.md — keep in sync -->`n`n"
   $content = [System.IO.File]::ReadAllText("docs/path/to/source.md", [System.Text.Encoding]::UTF8)
   [System.IO.File]::WriteAllText("docs/docfx/articles/target.md", $header + $content, [System.Text.Encoding]::UTF8)
   ```
3. **Never edit only the copy** — changes will be lost when the source is next synced
