# 🚫 AI Implementation Constraints

This document defines strict boundaries for AI-assisted code generation within the Opossum project.

---

## ⚠️ CRITICAL RULE: Sample Project is Manual Development Only

### Restricted Directory

**Path**: `Samples\Opossum.Samples.CourseManagement\`

**All files and subdirectories** within this path are **strictly prohibited** from AI code generation.

---

## What AI May Do

### ✅ Permitted AI Activities

1. **Answer Questions**
   - Explain how to use Opossum features
   - Clarify library APIs and patterns
   - Discuss best practices for event sourcing
   - Explain DCB specification concepts
   - Provide guidance on mediator pattern usage

2. **Provide Documentation**
   - Generate documentation for library code (not sample code)
   - Create usage guides and tutorials
   - Write API reference documentation
   - Explain architectural decisions

3. **Review Code**
   - Review manually-written sample code
   - Suggest improvements to manually-written code
   - Identify potential issues or bugs
   - Recommend patterns and practices

4. **Generate Tests**
   - Create unit tests for library code (in `src\Opossum\`)
   - Create integration tests in `tests\` directories
   - Generate test fixtures and helpers

5. **Implement Library Code**
   - Generate code in `src\Opossum\` (the library itself)
   - Create infrastructure code in `tests\` directories
   - Modify configuration files
   - Update build scripts and project files

---

## What AI May NOT Do

### ❌ Prohibited AI Activities

1. **Generate Sample Application Code**
   - DO NOT create files in `Samples\Opossum.Samples.CourseManagement\`
   - DO NOT modify existing files in sample directory
   - DO NOT write implementations for:
     - Domain events (e.g., `StudentEnlistedToCourseEvent`)
     - Aggregates (e.g., `CourseEnlistmentAggregate`)
     - Commands (e.g., `EnlistStudentToCourseCommand`)
     - Handlers (e.g., `EnlistStudentToCourseHandler`)
     - Any other domain logic in sample project

2. **Provide Copy-Paste Sample Code**
   - DO NOT give complete implementations to copy into sample project
   - Code examples in documentation are for **reference only**
   - Developer must type and understand all sample code personally

3. **Scaffold Sample Project Structure**
   - DO NOT create directory structures in sample project
   - DO NOT generate boilerplate files
   - DO NOT set up project configuration for sample

---

## Rationale

### Why This Constraint Exists

**To ensure the full developer experience of using the Opossum library.**

When developers manually write the sample application, they:

1. **Learn the API deeply** by typing every character
2. **Discover pain points** in the library's developer experience
3. **Test documentation** by following it manually
4. **Build muscle memory** for common patterns
5. **Catch usability issues** that automated generation would hide
6. **Validate assumptions** about library design
7. **Experience friction** that needs to be reduced

This manual work is **intentional and valuable** - it makes the library better.

---

## Scope of Restriction

### Files Explicitly Restricted

All files matching these patterns:

```
Samples\Opossum.Samples.CourseManagement\**\*.cs
Samples\Opossum.Samples.CourseManagement\**\*.csproj
Samples\Opossum.Samples.CourseManagement\**\*.json
Samples\Opossum.Samples.CourseManagement\**\*.*
```

**Examples of restricted files:**
- `Samples\Opossum.Samples.CourseManagement\Domain\Events.cs`
- `Samples\Opossum.Samples.CourseManagement\Domain\CourseEnlistmentAggregate.cs`
- `Samples\Opossum.Samples.CourseManagement\Domain\Commands.cs`
- `Samples\Opossum.Samples.CourseManagement\Domain\Handlers\*.cs`
- `Samples\Opossum.Samples.CourseManagement\Program.cs`
- `Samples\Opossum.Samples.CourseManagement\appsettings.json`

### Files NOT Restricted

All other files in the solution are fair game for AI assistance:

```
✅ src\Opossum\**\*.cs                    (library code)
✅ tests\Opossum.UnitTests\**\*.cs        (unit tests)
✅ tests\Opossum.IntegrationTests\**\*.cs (integration tests)
✅ Documentation\**\*.md                   (documentation)
✅ Specification\**\*.md                   (specifications)
✅ *.props, *.targets, *.csproj           (build files)
✅ Samples\*.sln                          (solution files)
```

**Examples of permitted files:**
- `src\Opossum\Configuration\OpossumOptions.cs`
- `src\Opossum\Storage\FileSystem\FileSystemEventStore.cs`
- `tests\Opossum.UnitTests\Configuration\OpossumOptionsTests.cs`
- `tests\Opossum.IntegrationTests\OpossumFixture.cs`
- `Documentation\implementation-ready.md`

---

## How to Interact with AI

### ✅ Good Requests

**Question about API:**
> "How do I append events with tags using Opossum?"

**Explanation request:**
> "Explain how the DCB specification handles optimistic concurrency"

**Review request:**
> "I wrote this CourseEnlistmentAggregate - can you review it for best practices?"

**Library implementation:**
> "Implement the FileSystemEventStore.AppendAsync() method"

**Test creation:**
> "Create unit tests for StorageInitializer"

### ❌ Bad Requests

**Direct code generation:**
> "Generate the StudentEnlistedToCourseEvent for me"
> ❌ NO - Developer must write this

**File creation:**
> "Create the CourseEnlistmentAggregate.cs file with Apply methods"
> ❌ NO - Developer must create this

**Boilerplate scaffolding:**
> "Set up the Domain folder structure in the sample project"
> ❌ NO - Developer must structure this

**Complete implementations:**
> "Write the EnlistStudentToCourseHandler with all validation logic"
> ❌ NO - Developer must implement this

---

## Enforcement

### AI Response Pattern

When asked to generate sample project code, AI should respond:

```
❌ I cannot generate code for Opossum.Samples.CourseManagement.

This sample project must be written manually to ensure you get 
the full developer experience of using the Opossum library.

However, I can:
✅ Explain how to implement [requested feature]
✅ Show you which Opossum APIs to use
✅ Discuss patterns and best practices
✅ Review code you've written manually

Would you like me to explain how to approach this instead?
```

### Documentation References

Code examples in documentation files are clearly marked:

```markdown
⚠️ MANUAL ONLY - Reference example (DO NOT auto-generate):
```

This signals that the code is for learning, not copying.

---

## Benefits of This Constraint

### What We Gain

1. **Real Usability Testing**
   - Developer friction becomes visible
   - Documentation gaps are discovered
   - API confusion is revealed

2. **Better Library Design**
   - Painful APIs get improved
   - Missing helpers get identified
   - Confusing patterns get simplified

3. **Authentic Learning**
   - Developer understands patterns deeply
   - Knowledge is internalized, not copy-pasted
   - Troubleshooting skills are developed

4. **Trustworthy Sample**
   - Sample code represents real usage
   - Patterns are validated by manual implementation
   - Example is genuinely helpful to other developers

---

## Summary

| Category | AI Allowed | Reason |
|----------|------------|--------|
| Library code (`src\Opossum\`) | ✅ Yes | This IS the product being built |
| Test code (`tests\`) | ✅ Yes | Validates the library |
| Documentation | ✅ Yes | Helps users understand the library |
| **Sample project** | ❌ **NO** | **Must test library as real user would** |

---

**Remember**: The sample project is not just an example - it's a **usability test**. 

By writing it manually, you become the first real user of your library, discovering issues before anyone else does.

This constraint **makes the library better** for everyone.

---

## Questions?

If unsure whether AI can help with something:

1. Is it in `Samples\Opossum.Samples.CourseManagement\`?
   - Yes → ❌ AI cannot generate code
   - No → ✅ AI can help

2. Are you asking AI to write sample domain logic?
   - Yes → ❌ Write it manually
   - No → ✅ AI can assist

3. Would this prevent you from experiencing the library as a real user?
   - Yes → ❌ Do it manually
   - No → ✅ AI can help

**When in doubt, write it manually.** You'll thank yourself later when you discover usability issues early.
