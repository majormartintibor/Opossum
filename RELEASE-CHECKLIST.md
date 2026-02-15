# NuGet Release Checklist - v0.1.0-preview.1

## ✅ Status Overview

| Category | Status | Notes |
|----------|--------|-------|
| **Project Configuration** | ❌ TODO | Need package metadata |
| **Versioning** | ❌ TODO | Need version properties |
| **Documentation** | ✅ DONE | README, LICENSE, CONTRIBUTING |
| **XML Documentation** | ✅ DONE | Generated and included |
| **Build & Test** | ✅ DONE | Builds successfully |
| **CHANGELOG** | ✅ DONE | Created |
| **Icon/Logo** | ✅ DONE | opossum.png included |
| **Git Tagging** | ❌ TODO | Tag before release |

---

## 📋 Detailed Checklist

### 1. ✅ Documentation (COMPLETE)

- [x] **README.md** - Comprehensive with quick start, API reference, use cases
- [x] **LICENSE** - MIT License with correct year and author
- [x] **CONTRIBUTING.md** - Complete contributor guide
- [x] All docs in `docs/` folder
- [x] Sample application code

### 2. ❌ Project File Configuration (TODO)

**File:** `src/Opossum/Opossum.csproj`

Add the following package metadata:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
  
  <!-- Package Information -->
  <PackageId>Opossum</PackageId>
  <Version>0.1.0-preview.1</Version>
  <Authors>Martin Tibor Major</Authors>
  <Company>Martin Tibor Major</Company>
  <Product>Opossum</Product>
  <Description>A file system-based event store for .NET that implements the DCB (Dynamic Consistency Boundaries) specification. Perfect for offline-first applications, on-premises deployments, and scenarios where simplicity matters more than cloud-scale distribution.</Description>
  <PackageTags>event-sourcing;eventsourcing;event-store;eventstore;dcb;cqrs;domain-driven-design;ddd;filesystem;offline-first;projections</PackageTags>
  <PackageProjectUrl>https://github.com/majormartintibor/Opossum</PackageProjectUrl>
  <RepositoryUrl>https://github.com/majormartintibor/Opossum</RepositoryUrl>
  <RepositoryType>git</RepositoryType>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageReadmeFile>README.md</PackageReadmeFile>
  <PackageReleaseNotes>First preview release of Opossum event store. See CHANGELOG.md for details.</PackageReleaseNotes>
  
  <!-- Documentation -->
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <DocumentationFile>bin\$(Configuration)\$(TargetFramework)\Opossum.xml</DocumentationFile>
  <NoWarn>$(NoWarn);1591</NoWarn> <!-- Suppress missing XML comment warnings for now -->
  
  <!-- Build Settings -->
  <Deterministic>true</Deterministic>
  <PublishRepositoryUrl>true</PublishRepositoryUrl>
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
  <DebugType>embedded</DebugType>
  
  <!-- NuGet Package Settings -->
  <IsPackable>true</IsPackable>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
</PropertyGroup>

<!-- Include README in package -->
<ItemGroup>
  <None Include="..\..\README.md" Pack="true" PackagePath="\" />
</ItemGroup>
```

### 3. ❌ XML Documentation (TODO)

**Current Status:** XML documentation file is NOT being generated.

**Action Required:**
1. The `GenerateDocumentationFile` property will be added in step 2
2. All public APIs should have XML comments (most already do)
3. Review and ensure all public APIs are documented:
   - `IEventStore`
   - `IProjectionStore`
   - `ICommand`
   - `IEvent`
   - Extension methods
   - Configuration classes

**XML Comments Coverage:**
- ✅ `OpossumOptions` - Already documented
- ✅ `IEventStore` - Already documented
- ✅ `IProjectionDefinition` - Already documented
- ⚠️ Need to verify all extension methods have XML comments

### 4. ❌ CHANGELOG.md (TODO)

**File:** `CHANGELOG.md` (create in solution root)

```markdown
# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0-preview.1] - 2025-02-11

### 🎉 First Preview Release

This is the first preview release of Opossum - a file system-based event store implementing the DCB (Dynamic Consistency Boundaries) specification.

### ✨ Features

#### Core Event Store
- **File-based storage** - Events stored as JSON files in structured directories
- **DCB implementation** - Full support for Dynamic Consistency Boundaries specification
- **Optimistic concurrency** - Append conditions for race-free operations
- **Tag-based indexing** - Fast event queries without full scans
- **Event Type indexing** - Efficient filtering by event type
- **Ledger system** - Monotonic sequence positions
- **Durability guarantees** - Configurable flush-to-disk behavior

#### Projection System
- **Materialized views** - Rebuild read models from events
- **Tag-based projection queries** - Fast projection lookups
- **Multi-stream projections** - Query related events across streams
- **Automatic rebuilding** - Rebuild projections from scratch
- **Assembly scanning** - Auto-discover projection definitions

#### Developer Experience
- **.NET 10 support** - Built for latest .NET
- **Dependency injection** - First-class DI support
- **ConfigureAwait(false)** - Library-safe async/await
- **Mediator pattern** - Built-in command/query handling
- **Fluent API** - Intuitive event building with extensions
- **Sample application** - Complete course management example

#### Configuration
- **Flexible configuration** - appsettings.json binding support
- **Platform-aware paths** - Handles Windows/Linux path differences
- **Validation** - Built-in options validation

### 📝 Documentation
- Comprehensive README with quick start guide
- API reference documentation
- Sample application demonstrating real-world usage
- CONTRIBUTING guide for contributors
- Use case documentation (automotive retail, POS systems, etc.)
- Performance characteristics and scalability limits

### ⚠️ Known Limitations (MVP)
- **Single context only** - Multi-context support planned for future release
- **No cache warming** - Feature planned but not in preview
- **Single-server deployments** - Not designed for distributed systems
- **File count limits** - Performance degrades beyond ~10M events

### 🎯 Target Use Cases
- On-premises applications
- Offline-first applications
- Small business ERP/POS systems
- Development & testing environments
- Compliance-heavy industries requiring audit trails
- Budget-conscious deployments avoiding cloud costs

### 📦 Package Information
- **Package ID:** Opossum
- **Target Framework:** .NET 10.0
- **License:** MIT
- **Repository:** https://github.com/majormartintibor/Opossum

### 🚀 Getting Started

```bash
dotnet add package Opossum --version 0.1.0-preview.1
```

See [README.md](README.md) for complete quick start guide.

### 🙏 Acknowledgments
- Inspired by the [DCB Specification](https://dcb.events/)
- Built for real-world use cases in automotive retail and SMB applications

---

## [Unreleased]

### Planned Features
- Multi-context support
- Cache warming for projections
- Snapshot support for aggregates
- Event schema versioning
- Retention policies
- Archiving and compression
- Cross-platform performance optimizations

[0.1.0-preview.1]: https://github.com/majormartintibor/Opossum/releases/tag/v0.1.0-preview.1
```

### 5. ❌ Versioning Strategy (TODO)

**Assembly Versioning:**

Add to `src/Opossum/Opossum.csproj`:

```xml
<PropertyGroup>
  <AssemblyVersion>0.1.0.0</AssemblyVersion>
  <FileVersion>0.1.0.0</FileVersion>
  <InformationalVersion>0.1.0-preview.1</InformationalVersion>
</PropertyGroup>
```

**Versioning Scheme:**
- **0.1.0-preview.1** - First preview release
- **0.1.0-preview.2** - Bug fixes/minor changes
- **0.1.0** - Stable release when ready
- **0.2.0** - Next feature release (multi-context support?)
- **1.0.0** - Production-ready release

### 6. ⚠️ Package Icon (OPTIONAL)

**Status:** No icon currently

**Options:**
1. **Skip for preview** - Many packages don't have icons initially
2. **Create simple icon** - Opossum emoji 🦘 or simple logo
3. **Use GitHub avatar** - Temporary solution

**If adding icon:**
```xml
<PropertyGroup>
  <PackageIcon>icon.png</PackageIcon>
</PropertyGroup>

<ItemGroup>
  <None Include="..\..\icon.png" Pack="true" PackagePath="\" />
</ItemGroup>
```

### 7. ✅ Build & Test (COMPLETE)

- [x] Solution builds successfully in **Release** configuration
- [x] All 579 unit tests pass
- [x] All 117 integration tests pass
- [x] No compiler warnings in release build
- [x] Sample application runs successfully

**Verification:**
```bash
dotnet build -c Release
dotnet test
dotnet run --project Samples/Opossum.Samples.CourseManagement
```

### 8. ❌ Pre-Release Validation (TODO)

**Before publishing:**

1. **Pack the NuGet package:**
   ```bash
   dotnet pack src/Opossum/Opossum.csproj -c Release -o ./nupkg
   ```

2. **Inspect the package:**
   ```bash
   # List package contents
   nuget.exe list -Source ./nupkg -AllVersions
   
   # Or use NuGetPackageExplorer
   ```

3. **Validate package contents:**
   - [ ] README.md is included
   - [ ] LICENSE is referenced correctly
   - [ ] XML documentation file is included
   - [ ] Only net10.0 DLL is included
   - [ ] Dependencies are correct
   - [ ] Symbols package (.snupkg) is generated

4. **Test installation locally:**
   ```bash
   # Create test project
   dotnet new console -n TestOpossum
   cd TestOpossum
   
   # Add local package
   dotnet add package Opossum --source ../nupkg
   
   # Verify it works
   dotnet build
   ```

### 9. ❌ Git Tagging & Release (TODO)

**Steps:**

1. **Commit all changes:**
   ```bash
   git add .
   git commit -m "Prepare v0.1.0-preview.1 release"
   ```

2. **Create annotated tag:**
   ```bash
   git tag -a v0.1.0-preview.1 -m "Release v0.1.0-preview.1 - First preview"
   ```

3. **Push tag to GitHub:**
   ```bash
   git push origin v0.1.0-preview.1
   ```

4. **Create GitHub Release:**
   - Go to: https://github.com/majormartintibor/Opossum/releases/new
   - Select tag: v0.1.0-preview.1
   - Title: "Opossum v0.1.0-preview.1 - First Preview Release"
   - Description: Copy from CHANGELOG.md
   - Mark as "pre-release"
   - Attach .nupkg and .snupkg files

### 10. ❌ NuGet Publishing (TODO)

**Prerequisites:**
1. NuGet.org account
2. API key from NuGet.org

**Publishing:**
```bash
# Publish to NuGet.org
dotnet nuget push ./nupkg/Opossum.0.1.0-preview.1.nupkg --source https://api.nuget.org/v3/index.json --api-key YOUR_API_KEY

# Verify package is live
# Check: https://www.nuget.org/packages/Opossum/
```

### 11. ⚠️ Post-Release Verification (TODO)

After publishing:

1. **Verify package is searchable:**
   - Search "Opossum" on nuget.org
   - Verify README renders correctly
   - Check dependencies are correct

2. **Test installation:**
   ```bash
   dotnet new console -n TestInstall
   cd TestInstall
   dotnet add package Opossum --version 0.1.0-preview.1
   dotnet build
   ```

3. **Verify IntelliSense:**
   - Open test project in VS
   - Type `IEventStore`
   - Verify XML documentation shows up

4. **Update README badges:**
   - NuGet version badge should update automatically
   - Download count badge

---

## 🚀 Quick Release Script

Once all checklist items are complete, use this script:

```bash
# 1. Ensure all tests pass
dotnet test

# 2. Pack the NuGet package
dotnet pack src/Opossum/Opossum.csproj -c Release -o ./nupkg

# 3. Inspect package (manual)
# Open ./nupkg/Opossum.0.1.0-preview.1.nupkg in NuGet Package Explorer

# 4. Commit and tag
git add .
git commit -m "Release v0.1.0-preview.1"
git tag -a v0.1.0-preview.1 -m "Release v0.1.0-preview.1 - First preview"
git push origin feature/nuget-release
git push origin v0.1.0-preview.1

# 5. Publish to NuGet (replace YOUR_API_KEY)
dotnet nuget push ./nupkg/Opossum.0.1.0-preview.1.nupkg --source https://api.nuget.org/v3/index.json --api-key YOUR_API_KEY

# 6. Create GitHub Release (manual on GitHub)
```

---

## 📝 Notes

### Cache Warming Feature
- **Status:** NOT part of MVP
- **Spec Location:** `docs/specifications/spec-002-cache-warming.md`
- **Action:** Keep spec for future release, no code changes needed

### MVP Limitations to Document
Already documented in:
- `docs/limitations/mvp-single-context.md`
- Code comments throughout
- README.md

### Breaking Changes Policy
For preview releases:
- Breaking changes are acceptable
- Document in CHANGELOG
- Prefix version with `-preview`

---

## ✅ Final Checklist

Before executing release:

- [ ] All code changes committed
- [ ] CHANGELOG.md created and updated
- [ ] Opossum.csproj updated with package metadata
- [ ] XML documentation verified
- [ ] All tests pass (579 unit + 117 integration)
- [ ] Package built and inspected
- [ ] Local installation tested
- [ ] Git tag created
- [ ] GitHub release created
- [ ] NuGet package published
- [ ] Package verified on NuGet.org

**Estimated Time:** 2-3 hours for full release process

---

**Created:** 2025-02-11
**Target Release Date:** TBD
**Release Manager:** Martin Tibor Major
