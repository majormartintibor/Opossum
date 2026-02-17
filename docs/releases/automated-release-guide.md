# Automated NuGet Release Guide

**Purpose:** Quick reference for executing automated NuGet releases for Opossum.  
**Last Updated:** 2025-02-11  
**Automation Level:** Steps 1-10 (fully automated), Steps 11-12 (manual verification)

> ⚠️ **SECURITY WARNING**: Never commit your actual NuGet API key to this document or any file in Git! Always use a placeholder in documentation and store the real key securely (password manager, environment variable, etc.). GitHub will block commits containing real API keys.

---

## Prerequisites

Before starting any release, ensure:

1. ✅ All code changes merged to `main` branch
2. ✅ All tests passing (696/696)
3. ✅ Build succeeds with 0 warnings
4. ✅ Version number updated in `src/Opossum/Opossum.csproj`
5. ✅ **CHANGELOG.md updated** under `## [Unreleased]` section
6. ✅ README.md reflects current version (if needed)
7. ✅ NuGet API key available
8. ✅ GitHub CLI (`gh`) installed and authenticated

---

## How to Trigger Automated Release

### Step 1: Provide Release Information

When ready to release, tell Copilot:

```
I'm ready to release version [VERSION_NUMBER] to NuGet.
Please run the automated release process.

API Key: [YOUR_NUGET_API_KEY]
Version: [e.g., 0.1.0-preview.2]
```

**Example:**
```
I'm ready to release version 0.1.0-preview.2 to NuGet.
Please run the automated release process.

API Key: oy2abc123xyz...
Version: 0.1.0-preview.2
```

### Step 2: Copilot Will Automate Steps 1-10

The automation will execute:

1. ✅ **Clean and Build** - `dotnet clean` + `dotnet build --configuration Release`
2. ✅ **Run Tests** - Full unit and integration test suite
3. ✅ **Verify Metadata** - Check `.csproj` version matches
4. ✅ **Build Package** - `dotnet pack` to create `.nupkg` and `.snupkg`
5. ✅ **Inspect Package** - Extract and verify contents (README, icon, DLL)
6. ✅ **Test Locally** - Install package in temp project to verify
7. ✅ **Create Git Tag** - `git tag -a v[VERSION] -m "Release version [VERSION]"`
8. ✅ **Publish to NuGet** - Push package to NuGet.org
9. ✅ **Push Git Tag** - `git push origin v[VERSION]`
10. ✅ **Create GitHub Release** - Use `gh release create` with release notes

**Expected Duration:** ~5-10 minutes (mostly test execution time)

### Step 3: Manual Verification (Steps 11-12)

After automation completes, **you** should:

1. **Wait 5-10 minutes** for NuGet indexing
2. **Verify package page**: https://www.nuget.org/packages/Opossum/[VERSION]
   - ✅ Metadata correct
   - ✅ README displayed
   - ✅ Icon displayed
   - ✅ License shown (MIT)
3. **Test installation** in a new project:
   ```powershell
   dotnet new console -n TestInstall
   cd TestInstall
   dotnet add package Opossum --version [VERSION]
   dotnet list package  # Verify it appears
   ```
4. **Optional:** Post announcements (GitHub Discussions, social media)

---

## What Gets Automated

### Build & Test Verification
- Clean solution
- Release build with 0 warnings check
- Full test suite (unit + integration)
- Package metadata validation

### Package Creation
- `dotnet pack` in Release configuration
- Output to `./nupkgs/` folder
- Creates both `.nupkg` (main) and `.snupkg` (symbols)

### Package Verification
- Extracts package contents
- Verifies critical files:
  - `README.md`
  - `opossum.png` icon
  - `lib/net10.0/Opossum.dll`
  - XML documentation
- Tests local installation

### Git & GitHub
- Creates annotated Git tag: `v[VERSION]`
- Pushes tag to GitHub
- Creates GitHub Release with:
  - Title: `v[VERSION] - [Release Name]`
  - Release notes from CHANGELOG
  - Marked as pre-release (if preview/alpha/beta)

### NuGet Publishing
- Pushes main package (`.nupkg`)
- Attempts symbol package (`.snupkg`) - may fail if no PDBs (OK)
- Uses provided API key
- Targets: `https://api.nuget.org/v3/index.json`

---

## Version Numbering Strategy

Follow **Semantic Versioning** (`MAJOR.MINOR.PATCH[-prerelease]`):

| Release Type | Version Example | When to Use |
|--------------|----------------|-------------|
| First Preview | `0.1.0-preview.1` | Initial preview release |
| Preview Update | `0.1.0-preview.2` | Bug fixes in preview |
| First Stable | `0.1.0` | First production-ready release |
| Patch Release | `0.1.1` | Bug fixes only (backward compatible) |
| Minor Release | `0.2.0` | New features (backward compatible) |
| Major Release | `1.0.0` | Breaking changes |

**Pre-release suffixes:**
- `-preview.N` - Early testing phase
- `-alpha.N` - Very early, unstable
- `-beta.N` - Feature complete, testing
- `-rc.N` - Release candidate

---

## Troubleshooting

### Build Fails with Warnings
**Solution:** Fix all warnings before release. Zero warnings policy is enforced.

```powershell
dotnet build --configuration Release
# Expected: "Build succeeded. 0 Warning(s)"
```

### Tests Fail
**Solution:** Do NOT proceed with release. Fix failing tests first.

```powershell
dotnet test tests/Opossum.UnitTests/
dotnet test tests/Opossum.IntegrationTests/
```

### NuGet Push Fails: 409 Conflict
**Cause:** Version already published (NuGet packages are immutable)

**Solution:**
1. Increment version number (e.g., `0.1.0-preview.2` → `0.1.0-preview.3`)
2. Update `src/Opossum/Opossum.csproj`
3. Commit changes
4. Restart release process

### NuGet Push Fails: 403 Forbidden
**Cause:** API key invalid or expired

**Solution:**
1. Go to: https://www.nuget.org/account/apikeys
2. Regenerate API key
3. Use new key in release command

### Symbol Package Push Fails: 400 Bad Request
**Cause:** No PDB files in package (using embedded PDBs)

**This is OK!** The main package was published successfully. Symbol package is optional.

### GitHub Release Fails: `gh` not found
**Cause:** GitHub CLI not in PATH

**Solution:**
```powershell
# Add to PATH or use full path
& "C:\Program Files\GitHub CLI\gh.exe" auth status
```

Or manually create release via GitHub web UI:
https://github.com/majormartintibor/Opossum/releases/new

### Package Not Appearing in NuGet Search
**Cause:** Indexing delay (normal)

**Solution:**
- Wait 5-10 minutes
- Direct link works immediately: `https://www.nuget.org/packages/Opossum/[VERSION]`
- Search takes longer to update

---

## Pre-Release Checklist

Before asking Copilot to run the release:

- [ ] All commits merged to `main`
- [ ] Version updated in `src/Opossum/Opossum.csproj`
- [ ] CHANGELOG.md updated under `## [Unreleased]` with all changes
- [ ] All 696 tests passing locally
- [ ] Build succeeds with 0 warnings
- [ ] README version badge updated (if showing specific version)
- [ ] Breaking changes documented (if any)
- [ ] NuGet API key ready
- [ ] GitHub CLI authenticated (`gh auth status`)

---

## Post-Release Actions

After successful release:

1. ✅ **Update CHANGELOG.md**:
   - Rename `## [Unreleased]` to `## [VERSION] - YYYY-MM-DD`
   - Add new `## [Unreleased]` section at the top
   
   ```markdown
   ## [Unreleased]
   
   ### Added
   - (future changes go here)
   
   ## [0.1.0-preview.1] - 2025-02-11
   
   ### Added
   - Initial preview release
   ```

2. ✅ **Bump version** for next development cycle:
   ```xml
   <!-- In src/Opossum/Opossum.csproj -->
   <Version>0.1.0-preview.2</Version>
   ```

3. ✅ **Commit version bump**:
   ```powershell
   git add src/Opossum/Opossum.csproj CHANGELOG.md
   git commit -m "chore: Bump version to 0.1.0-preview.2 for next release"
   git push origin main
   ```

4. ✅ **Verify package on NuGet.org** (after 5-10 min)
5. ✅ **Test installation** from NuGet
6. ✅ **Optional:** Post announcements

---

## Release History Reference

### How to View All Releases

**GitHub Tags:**
```powershell
git tag -l
# Shows: v0.1.0-preview.1, v0.1.0-preview.2, etc.
```

**GitHub Releases:**
https://github.com/majormartintibor/Opossum/releases

**NuGet Versions:**
https://www.nuget.org/packages/Opossum#versions-body-tab

**Local Package Cache:**
```powershell
ls ./nupkgs/
# Shows: Opossum.0.1.0-preview.1.nupkg, etc.
```

---

## Emergency Rollback

**Important:** NuGet packages **cannot be deleted** after publishing.

If you need to pull a package:

### Option 1: Unlist Package
```powershell
dotnet nuget delete Opossum [VERSION] `
  --api-key $NUGET_API_KEY `
  --source https://api.nuget.org/v3/index.json `
  --non-interactive
```
- Package removed from search results
- Still installable if version is known
- Recommended for minor issues

### Option 2: Deprecate Package
1. Go to: https://www.nuget.org/packages/Opossum/[VERSION]/Manage
2. Check "Deprecate this package version"
3. Specify alternate version
4. Add deprecation message

### Option 3: Publish Fixed Version
1. Increment version (e.g., `0.1.0-preview.2`)
2. Fix the issue
3. Release new version
4. Optionally unlist broken version

---

## API Key Security

**⚠️ CRITICAL: NEVER commit your NuGet API key to Git!**

### Best Practices:
1. ✅ Store in password manager
2. ✅ Regenerate periodically (every 6 months)
3. ✅ Use separate keys for different projects
4. ✅ Revoke immediately if compromised
5. ✅ Set expiration date when creating key

### Secure Key Usage:
```powershell
# Option 1: Session variable (most secure)
$NUGET_API_KEY = "your-key-here"  # Only in terminal session

# Option 2: User environment variable (persistent)
[Environment]::SetEnvironmentVariable("NUGET_API_KEY", "your-key", "User")
```

**NEVER:**
- ❌ Commit key to Git
- ❌ Share key in emails/chat
- ❌ Store in plain text files
- ❌ Use screenshot/paste in public places

---

## Quick Reference Commands

### Manual Release (if automation unavailable)

```powershell
# Navigate to solution root
cd D:\Codeing\FileSystemEventStoreWithDCB\Opossum

# 1. Clean and build
dotnet clean
dotnet build --configuration Release

# 2. Run tests
dotnet test tests/Opossum.UnitTests/ --configuration Release
dotnet test tests/Opossum.IntegrationTests/ --configuration Release

# 3. Build package
dotnet pack src/Opossum/Opossum.csproj --configuration Release --output ./nupkgs

# 4. Create Git tag
git tag -a v0.1.0-preview.2 -m "Release version 0.1.0-preview.2"

# 5. Publish to NuGet
$NUGET_API_KEY = "your-api-key"
dotnet nuget push ./nupkgs/Opossum.0.1.0-preview.2.nupkg `
  --api-key $NUGET_API_KEY `
  --source https://api.nuget.org/v3/index.json

# 6. Push tag to GitHub
git push origin v0.1.0-preview.2

# 7. Create GitHub release
gh release create v0.1.0-preview.2 `
  --title "v0.1.0-preview.2 - Bug Fixes" `
  --notes "See CHANGELOG.md for details" `
  --prerelease
```

---

## Automation Script Location

The automation logic is implemented in GitHub Copilot's memory based on:
- This document (`docs/releases/automated-release-guide.md`)
- Full process document (`docs/guides/nuget-release-process.md`)
- Copilot instructions (`.github/copilot-instructions.md`)

**To trigger:** Simply tell Copilot you're ready to release with version and API key.

---

## Related Documentation

- **Full NuGet Release Process**: `docs/guides/nuget-release-process.md` (detailed step-by-step)
- **CHANGELOG Guidelines**: See "CHANGELOG Maintenance" in `.github/copilot-instructions.md`
- **Semantic Versioning**: https://semver.org/
- **NuGet Documentation**: https://learn.microsoft.com/en-us/nuget/

---

## Release Timeline Example

### Release v0.1.0-preview.1 (2025-02-11)
- **Duration:** ~10 minutes
- **Tests:** 696/696 passed
- **Warnings:** 0
- **NuGet:** Published successfully
- **GitHub:** Tag and release created
- **Issues:** Symbol package failed (expected, no PDBs) - OK

### Future Release v0.1.0-preview.2 (Planned)
- **Expected Changes:** Bug fixes from preview 1
- **Version Bump:** `0.1.0-preview.1` → `0.1.0-preview.2`
- **Process:** Same automated steps

---

**Document Version:** 1.0  
**Next Review:** After next release cycle  
**Maintained By:** Opossum Development Team
