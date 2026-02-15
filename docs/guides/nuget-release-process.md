# NuGet Release Process for Opossum

**Version:** 0.1.0-preview.1  
**Date:** 2025-02-11  
**Author:** Opossum Development Team

---

## Overview

This document describes the step-by-step process for publishing Opossum to NuGet.org. This is the **official release process** for all Opossum versions.

### Prerequisites Checklist

Before starting the release process, ensure:

- [x] All code changes merged to `main` branch
- [x] All tests passing (696/696)
- [x] Build succeeds with 0 warnings
- [x] Version number updated in `src/Opossum/Opossum.csproj`
- [x] CHANGELOG.md updated with release notes
- [x] README.md reflects current version
- [x] LICENSE file present and correct
- [x] NuGet API key available

---

## Release Process

### Step 1: Final Pre-Release Verification

Run the complete test suite and build to ensure everything is ready:

```powershell
# Clean the solution
dotnet clean

# Build in Release configuration
dotnet build --configuration Release

# Verify build output
# Expected: "Build succeeded. 0 Warning(s)"
```

**Verify the output shows:**
- ✅ `Build succeeded`
- ✅ `0 Warning(s)`
- ✅ `0 Error(s)`

### Step 2: Run Full Test Suite

```powershell
# Run unit tests
dotnet test tests/Opossum.UnitTests/Opossum.UnitTests.csproj --configuration Release

# Run integration tests
dotnet test tests/Opossum.IntegrationTests/Opossum.IntegrationTests.csproj --configuration Release
```

**Verify:**
- ✅ All 579 unit tests pass
- ✅ All 117 integration tests pass
- ✅ Total: 696/696 tests passing

**Stop if any tests fail!** Do not proceed with release.

### Step 3: Verify Package Metadata

Open `src/Opossum/Opossum.csproj` and verify:

```xml
<PropertyGroup>
  <PackageId>Opossum</PackageId>
  <Version>0.1.0-preview.1</Version> <!-- ✅ Correct version -->
  <Authors>Martin Tibor Major</Authors>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageProjectUrl>https://github.com/majormartintibor/Opossum</PackageProjectUrl>
  <PackageReadmeFile>README.md</PackageReadmeFile>
  <PackageIcon>opossum.png</PackageIcon>
  <!-- ... other metadata ... -->
</PropertyGroup>
```

**Critical fields to verify:**
- ✅ `Version` matches the release version
- ✅ `PackageId` is correct
- ✅ `Authors` is filled in
- ✅ `PackageLicenseExpression` is set (MIT)
- ✅ `PackageProjectUrl` points to correct GitHub repository
- ✅ `PackageReadmeFile` and `PackageIcon` are included

### Step 4: Build NuGet Package

Build the NuGet package in Release configuration:

```powershell
# Navigate to solution root
cd D:\Codeing\FileSystemEventStoreWithDCB\Opossum

# Build the package
dotnet pack src/Opossum/Opossum.csproj --configuration Release --output ./nupkgs
```

**Expected output:**
```
Successfully created package 'D:\...\Opossum\nupkgs\Opossum.0.1.0-preview.1.nupkg'.
Successfully created package 'D:\...\Opossum\nupkgs\Opossum.0.1.0-preview.1.snupkg'.
```

**Verify package files created:**
- ✅ `nupkgs/Opossum.0.1.0-preview.1.nupkg` (main package)
- ✅ `nupkgs/Opossum.0.1.0-preview.1.snupkg` (symbol package for debugging)

### Step 5: Inspect Package Contents (Optional but Recommended)

Use NuGet Package Explorer or command line to inspect the package:

**Option A: Using NuGet Package Explorer (GUI)**
1. Download from: https://www.microsoft.com/store/productId/9WZDNCRDMDM3
2. Open `nupkgs/Opossum.0.1.0-preview.1.nupkg`
3. Verify:
   - ✅ README.md is included
   - ✅ opossum.png icon is included
   - ✅ DLL files are in `lib/net10.0/`
   - ✅ XML documentation file is included
   - ✅ Metadata tab shows correct information

**Option B: Using command line**
```powershell
# Extract package to inspect contents
Expand-Archive -Path ./nupkgs/Opossum.0.1.0-preview.1.nupkg -DestinationPath ./package-contents

# Verify critical files
Test-Path ./package-contents/README.md  # Should be True
Test-Path ./package-contents/opossum.png  # Should be True
Test-Path ./package-contents/lib/net10.0/Opossum.dll  # Should be True
```

**Clean up:**
```powershell
Remove-Item -Recurse -Force ./package-contents
```

### Step 6: Test Package Locally (Optional but Recommended)

Before publishing to NuGet.org, test the package locally:

```powershell
# Create a test project in a separate directory
cd $env:TEMP
mkdir OpossumPackageTest
cd OpossumPackageTest
dotnet new console -n TestApp
cd TestApp

# Add local package source
dotnet nuget add source "D:\Codeing\FileSystemEventStoreWithDCB\Opossum\nupkgs" --name LocalOpossum

# Install the package from local source
dotnet add package Opossum --version 0.1.0-preview.1 --source LocalOpossum

# Verify installation
dotnet list package
# Expected: Opossum    0.1.0-preview.1

# Clean up
cd ..
Remove-Item -Recurse -Force TestApp
dotnet nuget remove source LocalOpossum
```

### Step 7: Create Git Tag

Tag the release in Git before publishing:

```powershell
# Navigate to repository root
cd D:\Codeing\FileSystemEventStoreWithDCB\Opossum

# Create annotated tag
git tag -a v0.1.0-preview.1 -m "Release version 0.1.0-preview.1 - First preview release"

# Verify tag created
git tag -l "v0.1.0-preview.1"

# Note: Do NOT push the tag yet - wait until NuGet publish succeeds
```

### Step 8: Publish to NuGet.org

**IMPORTANT:** This step publishes the package publicly. It cannot be undone (only unlisted).

```powershell
# Set your NuGet API key (replace with your actual key)
# You can get your API key from: https://www.nuget.org/account/apikeys
$NUGET_API_KEY = "your-api-key-here"

# Publish main package
dotnet nuget push ./nupkgs/Opossum.0.1.0-preview.1.nupkg `
  --api-key $NUGET_API_KEY `
  --source https://api.nuget.org/v3/index.json

# Publish symbol package (for debugging support)
dotnet nuget push ./nupkgs/Opossum.0.1.0-preview.1.snupkg `
  --api-key $NUGET_API_KEY `
  --source https://api.nuget.org/v3/index.json
```

**Expected output:**
```
Pushing Opossum.0.1.0-preview.1.nupkg to 'https://www.nuget.org/api/v2/package'...
  PUT https://www.nuget.org/api/v2/package/
  Created https://www.nuget.org/api/v2/package/ 201ms
Your package was pushed.
```

**If you get an error:**
- ❌ `403 Forbidden` - Check your API key is valid
- ❌ `409 Conflict` - Package version already exists (increment version)
- ❌ `400 Bad Request` - Package metadata validation failed

**⏳ Indexing delay:** It may take 5-10 minutes for the package to appear in NuGet search.

### Step 9: Push Git Tag to GitHub

After successful NuGet publish, push the tag to GitHub:

```powershell
# Push the tag to GitHub
git push origin v0.1.0-preview.1
```

**Verify on GitHub:**
- Navigate to: https://github.com/majormartintibor/Opossum/tags
- ✅ Tag `v0.1.0-preview.1` should appear

### Step 10: Create GitHub Release

Create a release on GitHub with release notes:

**Option A: Via GitHub Web UI**
1. Go to: https://github.com/majormartintibor/Opossum/releases/new
2. Select tag: `v0.1.0-preview.1`
3. Release title: `v0.1.0-preview.1 - First Preview Release`
4. Description: Copy content from `CHANGELOG.md` for this version
5. Check: ✅ **This is a pre-release**
6. Click: **Publish release**

**Option B: Via GitHub CLI (gh)**
```powershell
# Install GitHub CLI if not installed: winget install GitHub.cli

# Extract release notes from CHANGELOG
$releaseNotes = @"
## 🎉 First Preview Release

This is the first preview release of Opossum - a file system-based event store implementing the DCB (Dynamic Consistency Boundaries) specification.

See [CHANGELOG.md](https://github.com/majormartintibor/Opossum/blob/main/CHANGELOG.md) for full details.

### Installation

``````bash
dotnet add package Opossum --version 0.1.0-preview.1
``````

### Documentation

- [README](https://github.com/majormartintibor/Opossum/blob/main/README.md)
- [NuGet Package](https://www.nuget.org/packages/Opossum/0.1.0-preview.1)
- [Use Cases](https://github.com/majormartintibor/Opossum/blob/main/docs/guides/use-cases.md)
"@

# Create release
gh release create v0.1.0-preview.1 `
  --title "v0.1.0-preview.1 - First Preview Release" `
  --notes $releaseNotes `
  --prerelease
```

### Step 11: Verify Package on NuGet.org

After 5-10 minutes, verify the package is available:

1. **Check package page:**
   - Visit: https://www.nuget.org/packages/Opossum/0.1.0-preview.1
   - ✅ Package metadata is correct
   - ✅ README is displayed
   - ✅ Icon is displayed
   - ✅ License is shown (MIT)
   - ✅ Dependencies are listed

2. **Test installation in a new project:**
   ```powershell
   cd $env:TEMP
   mkdir NuGetVerification
   cd NuGetVerification
   dotnet new console -n VerifyOpossum
   cd VerifyOpossum
   
   # Install from NuGet (with --prerelease flag for preview packages)
   dotnet add package Opossum --version 0.1.0-preview.1 --prerelease
   
   # Verify installation
   dotnet list package
   # Expected: Opossum    0.1.0-preview.1
   
   # Clean up
   cd ..
   Remove-Item -Recurse -Force VerifyOpossum
   ```

3. **Check NuGet statistics (after 24 hours):**
   - Download count
   - Package dependencies graph

### Step 12: Post-Release Communications

After successful release, announce on:

1. **GitHub Discussions**
   - Create announcement: https://github.com/majormartintibor/Opossum/discussions
   - Title: "🎉 Opossum 0.1.0-preview.1 Released!"
   - Content: Link to release notes, quick start guide

2. **Social Media (Optional)**
   - Twitter/X, LinkedIn, Reddit r/dotnet
   - Example: "Just released Opossum 0.1.0-preview.1 - a file-based event store for .NET! Perfect for offline-first apps. Check it out: [link]"

3. **Update README badges**
   - Verify NuGet badge shows correct version: [![NuGet](https://img.shields.io/nuget/v/Opossum.svg)](https://www.nuget.org/packages/Opossum/)

---

## Post-Release Checklist

- [ ] ✅ Package published to NuGet.org
- [ ] ✅ Symbol package published
- [ ] ✅ Git tag pushed to GitHub
- [ ] ✅ GitHub release created
- [ ] ✅ Package verified on NuGet.org
- [ ] ✅ Test installation successful
- [ ] ✅ Announcement posted (if applicable)
- [ ] ✅ Local `nupkgs/` folder cleaned up (optional)

---

## Troubleshooting

### Package Push Failed - 403 Forbidden

**Cause:** API key is invalid or expired

**Solution:**
1. Go to: https://www.nuget.org/account/apikeys
2. Regenerate your API key
3. Update `$NUGET_API_KEY` variable
4. Retry push

### Package Push Failed - 409 Conflict

**Cause:** Package version already exists on NuGet.org

**Solution:**
1. NuGet packages are **immutable** - you cannot replace an existing version
2. Increment the version number (e.g., `0.1.0-preview.2`)
3. Update `src/Opossum/Opossum.csproj` with new version
4. Rebuild package
5. Retry push

### Package Not Appearing in Search

**Cause:** NuGet indexing delay

**Solution:**
- Wait 5-10 minutes for indexing
- Direct link works immediately: `https://www.nuget.org/packages/Opossum/0.1.0-preview.1`
- Search may take longer

### Symbol Package Push Failed

**Cause:** Symbol server issues (less critical)

**Solution:**
- Symbol package is optional (helps with debugging)
- If it fails, the main package is still usable
- Can retry later or skip

---

## Version Bump for Next Release

After releasing `0.1.0-preview.1`, prepare for the next version:

```powershell
# Update version in .csproj
# Change: <Version>0.1.0-preview.1</Version>
# To:     <Version>0.1.0-preview.2</Version>

# Update CHANGELOG.md
# Add:
# ## [Unreleased]
# 
# ### Added
# - (features for next release)
# 
# ### Fixed
# - (bugs fixed for next release)

# Commit changes
git add src/Opossum/Opossum.csproj CHANGELOG.md
git commit -m "Bump version to 0.1.0-preview.2 for next release"
git push origin main
```

---

## Security Considerations

### API Key Management

**⚠️ NEVER commit your NuGet API key to Git!**

**Best practices:**
1. Store API key in environment variable or password manager
2. Use separate API keys for different projects
3. Regenerate keys periodically
4. Revoke keys immediately if compromised

**Setting API key as environment variable:**
```powershell
# In PowerShell (temporary - session only)
$env:NUGET_API_KEY = "your-key-here"

# In PowerShell (permanent - user profile)
[Environment]::SetEnvironmentVariable("NUGET_API_KEY", "your-key-here", "User")

# Then use in push command:
dotnet nuget push ./nupkgs/Opossum.0.1.0-preview.1.nupkg `
  --api-key $env:NUGET_API_KEY `
  --source https://api.nuget.org/v3/index.json
```

---

## Rollback Plan

**Important:** NuGet packages **cannot be deleted** once published. They can only be **unlisted**.

### If You Need to Unlist a Package

If you discover a critical bug or security issue after publishing:

**Option 1: Unlist the package (recommended)**
```powershell
# Unlist package from search results (but still installable if version is known)
dotnet nuget delete Opossum 0.1.0-preview.1 `
  --api-key $NUGET_API_KEY `
  --source https://api.nuget.org/v3/index.json `
  --non-interactive
```

**Option 2: Publish a fixed version**
1. Increment version (e.g., `0.1.0-preview.2`)
2. Fix the bug
3. Publish new version
4. Optionally unlist the broken version

**Option 3: Mark as deprecated**
1. Go to: https://www.nuget.org/packages/Opossum/0.1.0-preview.1/Manage
2. Check "Deprecate this package version"
3. Specify alternate package or version
4. Add deprecation message

---

## Quick Reference Commands

```powershell
# Full release workflow (one script)
cd D:\Codeing\FileSystemEventStoreWithDCB\Opossum

# 1. Clean and build
dotnet clean
dotnet build --configuration Release

# 2. Run tests
dotnet test tests/Opossum.UnitTests/Opossum.UnitTests.csproj --configuration Release
dotnet test tests/Opossum.IntegrationTests/Opossum.IntegrationTests.csproj --configuration Release

# 3. Build package
dotnet pack src/Opossum/Opossum.csproj --configuration Release --output ./nupkgs

# 4. Create Git tag
git tag -a v0.1.0-preview.1 -m "Release version 0.1.0-preview.1"

# 5. Publish to NuGet
dotnet nuget push ./nupkgs/Opossum.0.1.0-preview.1.nupkg `
  --api-key $NUGET_API_KEY `
  --source https://api.nuget.org/v3/index.json

dotnet nuget push ./nupkgs/Opossum.0.1.0-preview.1.snupkg `
  --api-key $NUGET_API_KEY `
  --source https://api.nuget.org/v3/index.json

# 6. Push tag to GitHub
git push origin v0.1.0-preview.1

# 7. Create GitHub release (via web UI or gh CLI)
```

---

## Additional Resources

- [NuGet Documentation](https://learn.microsoft.com/en-us/nuget/)
- [Semantic Versioning](https://semver.org/)
- [Keep a Changelog](https://keepachangelog.com/)
- [GitHub Releases Guide](https://docs.github.com/en/repositories/releasing-projects-on-github)

---

**Document Version:** 1.0  
**Last Updated:** 2025-02-11  
**Next Review:** After first release cycle
