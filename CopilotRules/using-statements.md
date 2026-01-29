# Using Statements Management

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
