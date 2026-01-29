# NuGet Package Management Rules

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
