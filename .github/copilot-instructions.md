# Copilot Instructions

## Language and Framework

Use .NET 10 and C# 14
Prefer using the newest language features

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

## Compiler
All code you submit must compile without errors.

## Testing

### Framework
Use xUnit for all tests.

### Unit Tests
Unit Test project may only contain tests that work on pure data without any external dependencies.
No mocking is allowed in Unit Tests.
Currently this rule is violated, you must fix this the next time you work and refactor accordingly. You can then remove this line.

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
    options.AddContext("OpossumSampleApp");
    //TODO: multiple context support
    //options.AddContext("ExampleAdditionalContext");
});
```
Never use "D:\\Database" directly in tests.

### New features
When adding new features make sure to add sufficient unit and integration tests to cover the new functionality.
All tests must pass before committing new code.
