# Using Statements Best Practices - Opossum Sample Application

## 📋 Summary

This document outlines how to manage using statements in the Opossum.Samples.CourseManagement application.

## ✅ Core Principles

### 1. **Global Usings** (GlobalUsings.cs)
Place **external dependencies** that are used across **most files** in the sample application.

**Current GlobalUsings.cs:**
```csharp
// Global using directives for external dependencies
// Opossum.* namespaces should remain in individual files for clarity

global using Microsoft.AspNetCore.Mvc;
```

**When to add to GlobalUsings.cs:**
- ✅ External namespaces used in 3+ feature folders
- ✅ Common framework namespaces (Microsoft.*, System.*)
- ❌ Opossum.* namespaces (keep in individual files)
- ❌ Feature-specific namespaces

### 2. **File-Level Usings** (Individual .cs files)
Keep these in each file for **clarity and maintainability**:

**Always keep in files:**
```csharp
using Opossum.Core;           // ✅ Core Opossum namespace
using Opossum.Extensions;      // ✅ Core Opossum namespace
using Opossum.Mediator;        // ✅ Core Opossum namespace
```

**Feature-specific usings:**
```csharp
using Opossum.Samples.CourseManagement.StudentRegistration;  // ✅ Cross-feature reference
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;  // ✅ Type alias
```

## 📂 File Organization Example

### ✅ GOOD - CreateCourse.cs
```csharp
using Opossum.Core;
using Opossum.Extensions;
using Opossum.Mediator;

namespace Opossum.Samples.CourseManagement.CourseCreation;

public static class Endpoint
{
    public static void MapCreateCourseEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/courses", async (
            [FromBody] CreateCourseRequest request,  // FromBody works because Microsoft.AspNetCore.Mvc is global
            [FromServices] IMediator mediator) =>
        {
            // ... implementation
        });
    }
}
```

### ❌ BAD - Don't do this
```csharp
// ❌ BAD: Mixing external and internal namespaces makes it unclear
using Microsoft.AspNetCore.Mvc;
using Opossum.Core;
using System.Text.Json;  // ❌ Should be global if used frequently
using Opossum.Extensions;

namespace Opossum.Samples.CourseManagement.CourseCreation;
```

## 🎯 Decision Flow Chart

When adding a using statement:

```
Is it an Opossum.* namespace?
├─ YES → Keep in the .cs file ✅
│   └─ Makes internal dependencies visible
│
└─ NO → Is it used in 3+ feature folders?
    ├─ YES → Add to GlobalUsings.cs with `global using` ✅
    │   └─ Reduces boilerplate
    │
    └─ NO → Keep in the .cs file ✅
        └─ Avoids polluting global scope
```

## 📊 Current Global Usings

| Namespace | Reason | Usage Count |
|-----------|--------|-------------|
| `Microsoft.AspNetCore.Mvc` | Required for `[FromBody]`, `[FromServices]` attributes | All endpoint files |

## 🔄 Future Candidates for Global Usings

Monitor these namespaces - if used frequently, consider adding to GlobalUsings.cs:

- `System.Text.Json` - if custom JSON serialization is needed
- `Microsoft.Extensions.Logging` - if logging is added to handlers
- Any other external package used across multiple features

## 🚫 Never Add to Global Usings

- ❌ `Opossum.*` namespaces (violates Opossum guidelines)
- ❌ Feature-specific namespaces (`*.StudentRegistration`, `*.CourseCreation`)
- ❌ Type aliases (`using Tier = ...`)
- ❌ Namespaces used in only 1-2 files

## 📝 Maintenance Guidelines

### When Adding New Feature Folders

1. Start with only Opossum.* usings:
   ```csharp
   using Opossum.Core;
   using Opossum.Extensions;
   using Opossum.Mediator;
   ```

2. Add feature-specific usings as needed:
   ```csharp
   using Opossum.Samples.CourseManagement.OtherFeature;
   ```

3. Rely on GlobalUsings.cs for external dependencies

### When Refactoring

1. Remove any `using Microsoft.AspNetCore.Mvc;` statements
2. Keep all `using Opossum.*;` statements
3. Verify the file still compiles (GlobalUsings provides external namespaces)

## ✨ Benefits of This Approach

1. **Clarity** - Internal dependencies (Opossum.*) are visible in each file
2. **Maintainability** - Easy to see what each file depends on
3. **Reduced Boilerplate** - Common external usings are global
4. **Consistency** - Follows Opossum library conventions

## 🔗 Related Documentation

- See `/CopilotRules/using-statements.md` for core Opossum library rules
- See `src/Opossum/GlobalUsings.cs` for library global usings
- See `tests/Opossum.UnitTests/GlobalUsings.cs` for test project global usings
