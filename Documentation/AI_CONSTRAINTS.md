# 🚫 AI Implementation Constraints

This document defines strict boundaries for AI-assisted code generation within the Opossum project.

---

## ✅ CONSTRAINT REVOKED: Sample Project May Now Use AI Assistance

### Status: **REVOKED** (as of manual implementation completion)

**Original Path**: `Samples\Opossum.Samples.CourseManagement\`

**Previous Rule**: All files within this path were strictly prohibited from AI code generation.

**Current Status**: ✅ **AI may now generate code in the sample project**

### Why This Was Revoked

The developer has **successfully completed manual implementation** of core sample features, demonstrating:

1. ✅ **API Mastery** - Fluent Event Builder, Mediator Pattern, CommandResult, BuildProjections
2. ✅ **Real Issues Found** - Missing DI registration, folder naming bugs, Swagger conflicts
3. ✅ **Architecture Understanding** - Event sourcing, CQRS, projections, tag-based querying
4. ✅ **Manual Features Implemented**:
   - RegisterStudent (command with event appending)
   - GetStudentsShortInfo (query with projections)
   - UpdateStudentSubscription (command with validation)
   - CreateCourse (entity creation)

**The developer has experienced the library sufficiently.** AI acceleration is now permitted.

---

## ⚠️ Historical Context (For Reference)

### Original Rationale

The constraint existed to ensure the full developer experience of using the Opossum library.

Manual development allowed the developer to:
1. Learn the API deeply by typing every character
2. Discover pain points in the library's developer experience
3. Test documentation by following it manually
4. Build muscle memory for common patterns
5. Catch usability issues that automated generation would hide
6. Validate assumptions about library design
7. Experience friction that needs to be reduced

**This manual work was intentional and valuable** - it made the library better.

### What Was Accomplished

Through manual implementation, the following improvements were made:
- ✅ Fixed missing `IMediator` DI registration
- ✅ Corrected folder naming inconsistencies (Indeces → Indices)
- ✅ Added `CommandResult<T>` pattern to core library
- ✅ Implemented `BuildProjections<T>()` extension method
- ✅ Established global usings best practices
- ✅ Resolved Swagger endpoint naming conflicts

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
   - Generate documentation for library code
   - Create usage guides and tutorials
   - Write API reference documentation
   - Explain architectural decisions

3. **Review Code**
   - Review sample code (manually-written or AI-generated)
   - Suggest improvements
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

6. **Generate Sample Application Code** ✅ **NEW**
   - Generate code in `Samples\Opossum.Samples.CourseManagement\`
   - Create domain events, commands, handlers
   - Implement query handlers and projections
   - Build endpoints and request/response models
   - **Constraint lifted after manual experience validation**

---

## What AI Should Be Cautious About

### ⚠️ Areas Requiring Care

While AI may now assist with sample project code, consider:

1. **Maintain Established Patterns**
   - Follow the patterns demonstrated in manually-written features
   - Use `CommandResult<T>` for all handlers
   - Apply `.ToDomainEvent().WithTag().WithTimestamp()` consistently
   - Structure files per established conventions

2. **Preserve Manual Learnings**
   - Don't undo improvements discovered during manual development
   - Keep global usings organization
   - Maintain endpoint naming conventions
   - Follow established validation patterns

3. **Business Logic Validation**
   - Ensure new features align with domain requirements
   - Validate aggregate consistency rules
   - Consider concurrency implications
   - Think about event ordering and projections

---

## ~~What AI May NOT Do~~ (DEPRECATED)

### ❌ ~~Prohibited AI Activities~~ (CONSTRAINT REVOKED)

~~1. **Generate Sample Application Code**~~
   - ~~DO NOT create files in `Samples\Opossum.Samples.CourseManagement\`~~
   - ~~DO NOT modify existing files in sample directory~~

**Status**: ✅ This constraint is no longer active. AI may now assist with sample code.

~~2. **Provide Copy-Paste Sample Code**~~
   - ~~DO NOT give complete implementations to copy into sample project~~

**Status**: ✅ Complete implementations may now be provided when appropriate.

~~3. **Scaffold Sample Project Structure**~~
   - ~~DO NOT create directory structures in sample project~~

**Status**: ✅ AI may now scaffold structures following established patterns.

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

## Scope of Current AI Assistance

### Files Now Available for AI Generation

All files in the solution are now available for AI assistance:

```
✅ src\Opossum\**\*.cs                    (library code)
✅ tests\Opossum.UnitTests\**\*.cs        (unit tests)
✅ tests\Opossum.IntegrationTests\**\*.cs (integration tests)
✅ Documentation\**\*.md                   (documentation)
✅ Specification\**\*.md                   (specifications)
✅ *.props, *.targets, *.csproj           (build files)
✅ Samples\**\*.cs                        (sample application) ⭐ NEW
✅ Samples\*.sln                          (solution files)
```

**Examples now include:**
- `Samples\Opossum.Samples.CourseManagement\CourseCreation\CreateCourse.cs` ✅
- `Samples\Opossum.Samples.CourseManagement\StudentRegistration\RegisterStudent.cs` ✅
- `Samples\Opossum.Samples.CourseManagement\StudentEnlistment\EnlistStudent.cs` ✅ (future)
- All other sample application code ✅

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

**Sample code generation:** ✅ **NOW PERMITTED**
> "Generate the StudentEnlistedToCourseEvent and handler"
> "Create the CourseEnlistmentAggregate with Apply methods"
> "Implement the EnlistStudentToCourseHandler with validation logic"

### ⚠️ Requests Requiring Context

**Feature implementation:**
> "Set up the student enrollment feature"
> 💡 AI should ask clarifying questions about business rules, validation requirements, etc.

**Aggregate design:**
> "Create the Course aggregate"
> 💡 AI should understand the domain model and event sequences before generating

---

## ~~Enforcement~~ Historical Reference

### ~~AI Response Pattern~~ (DEPRECATED)

~~When asked to generate sample project code, AI should respond:~~

```
❌ (OLD) I cannot generate code for Opossum.Samples.CourseManagement.

✅ (NEW) I can help generate code for the sample project.

Let me understand your requirements:
- What business rules should this feature enforce?
- What events need to be raised?
- Are there any validation requirements?
- Should this use an aggregate or simple command handler?
```

---

## Benefits of the Original Constraint (Historical)

### What Was Gained Through Manual Development

1. **Real Usability Testing**
   - Developer friction became visible
   - Documentation gaps were discovered
   - API confusion was revealed

2. **Better Library Design**
   - Painful APIs were improved
   - Missing helpers were identified (CommandResult, BuildProjections)
   - Confusing patterns were simplified

3. **Authentic Learning**
   - Developer understands patterns deeply
   - Knowledge is internalized, not copy-pasted
   - Troubleshooting skills were developed

4. **Trustworthy Sample**
   - Sample code represents real usage
   - Patterns are validated by manual implementation
   - Example is genuinely helpful to other developers

**These benefits were achieved.** AI assistance is now safe to use.

---

## Summary

| Category | AI Allowed | Status |
|----------|------------|--------|
| Library code (`src\Opossum\`) | ✅ Yes | Active |
| Test code (`tests\`) | ✅ Yes | Active |
| Documentation | ✅ Yes | Active |
| **Sample project** | ✅ **Yes** | **✅ CONSTRAINT REVOKED** |

---

**Updated Rule**: The sample project constraint has been lifted after successful manual implementation validated the library's developer experience.

AI may now assist with all aspects of the Opossum project, including sample application development.

---

## Questions?

If unsure whether AI can help with something:

1. Is it Opossum-related?
   - Yes → ✅ AI can assist
   - No → Ask for general programming help

2. Does it require understanding business domain rules?
   - Yes → 💡 Provide context to AI first
   - No → ✅ AI can proceed

3. Is it modifying core library behavior?
   - Yes → ⚠️ Consider impact carefully
   - No → ✅ Proceed with implementation

**AI assistance is now available for the entire Opossum project, including samples.**

The manual development phase successfully validated the library's usability. AI acceleration is now appropriate and beneficial.
