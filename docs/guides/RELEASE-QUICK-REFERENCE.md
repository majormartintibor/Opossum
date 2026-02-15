# Quick Release Commands - Opossum 0.1.0-preview.1

**⚡ Use this as a quick reference when executing the release**

---

## Pre-Flight Check

```powershell
# Verify all tests pass
dotnet test

# Verify build succeeds with 0 warnings
dotnet build --configuration Release 2>&1 | Select-String "Warning"
# Expected: "0 Warning(s)"
```

---

## Release Commands (Copy & Execute)

```powershell
# 1️⃣ Navigate to repository root
cd D:\Codeing\FileSystemEventStoreWithDCB\Opossum

# 2️⃣ Clean solution
dotnet clean

# 3️⃣ Build in Release configuration
dotnet build --configuration Release

# 4️⃣ Run all tests
dotnet test --configuration Release

# 5️⃣ Build NuGet package
dotnet pack src/Opossum/Opossum.csproj --configuration Release --output ./nupkgs

# 6️⃣ Create Git tag (local only - don't push yet)
git tag -a v0.1.0-preview.1 -m "Release version 0.1.0-preview.1 - First preview release"

# 7️⃣ Publish to NuGet.org
# ⚠️ REPLACE 'YOUR_API_KEY' with your actual NuGet API key!
dotnet nuget push ./nupkgs/Opossum.0.1.0-preview.1.nupkg `
  --api-key YOUR_API_KEY `
  --source https://api.nuget.org/v3/index.json

# 8️⃣ Publish symbol package (for debugging)
dotnet nuget push ./nupkgs/Opossum.0.1.0-preview.1.snupkg `
  --api-key YOUR_API_KEY `
  --source https://api.nuget.org/v3/index.json

# 9️⃣ Push Git tag to GitHub (only after NuGet publish succeeds)
git push origin v0.1.0-preview.1
```

---

## GitHub Release (Web UI)

After pushing the tag:

1. Go to: https://github.com/majormartintibor/Opossum/releases/new
2. Select tag: `v0.1.0-preview.1`
3. Title: `v0.1.0-preview.1 - First Preview Release`
4. Description: Copy from `CHANGELOG.md` section for 0.1.0-preview.1
5. ✅ Check: **This is a pre-release**
6. Click: **Publish release**

---

## Verification Steps

```powershell
# Wait 5-10 minutes after publish, then:

# 1. Check package page
start https://www.nuget.org/packages/Opossum/0.1.0-preview.1

# 2. Test installation in fresh project
cd $env:TEMP
mkdir OpossamTest
cd OpossamTest
dotnet new console
dotnet add package Opossum --version 0.1.0-preview.1 --prerelease
dotnet list package  # Verify Opossum 0.1.0-preview.1 appears
cd ..
Remove-Item -Recurse -Force OpossamTest
```

---

## Quick Troubleshooting

| Issue | Solution |
|-------|----------|
| **403 Forbidden** | Check API key is valid at https://www.nuget.org/account/apikeys |
| **409 Conflict** | Version already exists - cannot overwrite! Increment version. |
| **400 Bad Request** | Package metadata validation failed - check .csproj |
| **Package not in search** | Wait 5-10 minutes for indexing. Direct URL works immediately. |

---

## One-Liner Release (Advanced)

```powershell
# ⚠️ Only use if you're confident everything is ready!
# Set API key first: $NUGET_API_KEY = "your-key-here"

cd D:\Codeing\FileSystemEventStoreWithDCB\Opossum; `
dotnet clean; `
dotnet build --configuration Release; `
dotnet test --configuration Release; `
dotnet pack src/Opossum/Opossum.csproj --configuration Release --output ./nupkgs; `
git tag -a v0.1.0-preview.1 -m "Release 0.1.0-preview.1"; `
dotnet nuget push ./nupkgs/Opossum.0.1.0-preview.1.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json; `
dotnet nuget push ./nupkgs/Opossum.0.1.0-preview.1.snupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json; `
git push origin v0.1.0-preview.1
```

---

## Post-Release

```powershell
# Clean up local package files (optional)
Remove-Item -Recurse -Force ./nupkgs

# Bump version for next release
# Edit src/Opossum/Opossum.csproj
# Change: <Version>0.1.0-preview.1</Version>
# To:     <Version>0.1.0-preview.2</Version>

# Update CHANGELOG.md with [Unreleased] section
# Commit and push
git add src/Opossum/Opossum.csproj CHANGELOG.md
git commit -m "Bump version to 0.1.0-preview.2 for next release"
git push origin main
```

---

**📚 Full Documentation:** See `docs/guides/nuget-release-process.md`

**🎯 Quick Status:** See `docs/guides/release-status.md`

**✅ Everything Ready?** Execute the commands above!
