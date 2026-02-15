# Contributing to Opossum

First off, thank you for considering contributing to Opossum! 🦘

We welcome contributions from the community, whether it's:
- 🐛 Bug reports
- 💡 Feature requests
- 📖 Documentation improvements
- 🔧 Code contributions
- 🧪 Additional tests

## 📋 Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [Coding Standards](#coding-standards)
- [Testing Requirements](#testing-requirements)
- [Pull Request Process](#pull-request-process)
- [Definition of Done](#definition-of-done)
- [Project Structure](#project-structure)

---

## Code of Conduct

This project adheres to a simple code of conduct:

- **Be respectful** - Treat everyone with respect and kindness
- **Be constructive** - Provide constructive feedback
- **Be collaborative** - Work together towards common goals
- **Be professional** - Keep discussions focused on the project

---

## Getting Started

### Prerequisites

- **.NET 10 SDK** - [Download here](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Git** - For version control
- **Visual Studio 2026** (recommended) or **Visual Studio Code**

### Fork & Clone

1. Fork the repository on GitHub
2. Clone your fork locally:

```bash
git clone https://github.com/YOUR-USERNAME/Opossum.git
cd Opossum
```

3. Add the upstream repository:

```bash
git remote add upstream https://github.com/majormartintibor/Opossum.git
```

### Build the Project

```bash
dotnet build
```

### Run Tests

```bash
# Unit tests
dotnet test tests/Opossum.UnitTests/

# Integration tests
dotnet test tests/Opossum.IntegrationTests/

# All tests
dotnet test
```

---

## Development Setup

### IDE Configuration

**Visual Studio 2026:**
- Open `Opossum.slnx`
- All projects will load automatically

**Visual Studio Code:**
- Install C# Dev Kit extension
- Open the workspace folder

### Solution Structure

```
Opossum/
├── src/
│   └── Opossum/                    # Core library
├── tests/
│   ├── Opossum.UnitTests/          # Unit tests (no external dependencies)
│   └── Opossum.IntegrationTests/   # Integration tests (file system)
├── Samples/
│   ├── Opossum.Samples.CourseManagement/  # Sample web API
│   └── Opossum.Samples.DataSeeder/        # Data seeding tool
├── docs/                           # All documentation
└── Specification/                  # DCB specification
```

---

## Coding Standards

Opossum follows strict coding standards to maintain quality and consistency. **Please read [`.github/copilot-instructions.md`](.github/copilot-instructions.md) for complete guidelines.**

### Key Standards

#### 1. Language & Framework

- **Target:** .NET 10, C# 14
- **Use modern language features** (file-scoped namespaces, record types, pattern matching)

#### 2. Namespace & Using Statements

✅ **File-scoped namespaces:**
```csharp
namespace Opossum.Storage;  // ✅ Correct

// ❌ Wrong:
namespace Opossum.Storage { }
```

✅ **Using statement rules:**
- `Opossum.*` usings stay **in .cs files**
- External usings go in **GlobalUsings.cs**

```csharp
// In MyClass.cs
using Opossum.Configuration;  // ✅ Internal - stays here
using Opossum.Storage;        // ✅ Internal - stays here

namespace Opossum.DependencyInjection;
```

#### 3. Async/Await Best Practices

**CRITICAL:** Opossum is a library. **ALL** `await` statements **MUST** use `.ConfigureAwait(false)`:

```csharp
✅ var data = await File.ReadAllTextAsync(path).ConfigureAwait(false);
❌ var data = await File.ReadAllTextAsync(path);  // Missing ConfigureAwait!
```

**Why:** Prevents deadlocks when consumed by UI applications (WPF, Blazor, etc.)

#### 4. Package Management

**Central Package Management (CPM) is MANDATORY:**

❌ **Never add versions to .csproj:**
```xml
❌ <PackageReference Include="PackageName" Version="1.0.0" />
```

✅ **Add version to Directory.Packages.props:**
```xml
<!-- In Directory.Packages.props -->
<PackageVersion Include="PackageName" Version="1.0.0" />

<!-- In YourProject.csproj -->
<PackageReference Include="PackageName" />
```

#### 5. Documentation

- All `.md` files go in **`docs/`** folder
- Update **`Opossum.slnx`** when adding docs
- Use XML comments for public APIs

#### 6. External Libraries

**For `src/Opossum/`:**
- ❌ **No external libraries** except official Microsoft packages
- ✅ Use built-in .NET APIs

**For `Samples/`:**
- ✅ Ask permission before adding external dependencies

---

## Testing Requirements

### Test Philosophy

- **Unit Tests** - Pure data, no external dependencies, **no mocking**
- **Integration Tests** - File system tests, **no mocking**
- **Test Isolation** - Each test is independent
- **Temporary Directories** - Never use production paths in tests

### Writing Tests

#### Unit Tests (`tests/Opossum.UnitTests/`)

```csharp
[Fact]
public void AddContext_WithValidName_AddsContext()
{
    // Arrange
    var options = new OpossumOptions();

    // Act
    var result = options.AddContext("CourseManagement");

    // Assert
    Assert.Single(options.Contexts);
    Assert.Contains("CourseManagement", options.Contexts);
}
```

#### Integration Tests (`tests/Opossum.IntegrationTests/`)

```csharp
public class MyIntegrationTests : IDisposable
{
    private readonly string _testDirectory;

    public MyIntegrationTests()
    {
        // Create unique temp directory for this test class
        _testDirectory = Path.Combine(Path.GetTempPath(), $"OpossumTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    [Fact]
    public async Task MyTest()
    {
        // Use _testDirectory, never hardcoded paths
        var options = new OpossumOptions
        {
            RootPath = _testDirectory
        };
        options.AddContext("TestContext");
        
        // Test logic...
    }

    public void Dispose()
    {
        // Cleanup
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}
```

### Test Coverage Requirements

When adding a new feature:
1. ✅ Add **unit tests** for pure logic
2. ✅ Add **integration tests** for file system interactions
3. ✅ Ensure **all existing tests still pass**
4. ✅ Aim for high coverage (especially public APIs)

---

## Pull Request Process

### 1. Create a Feature Branch

```bash
git checkout -b feature/my-awesome-feature
# or
git checkout -b bugfix/fix-issue-123
```

### 2. Make Your Changes

- Follow coding standards
- Write tests
- Update documentation if needed

### 3. Commit Your Changes

Use clear, descriptive commit messages:

```bash
git commit -m "Add support for custom event serialization"
git commit -m "Fix: Handle null tags in QueryItem validation"
```

### 4. Run All Tests

```bash
dotnet test
```

**All tests must pass before submitting PR.**

### 5. Push to Your Fork

```bash
git push origin feature/my-awesome-feature
```

### 6. Submit Pull Request

- Go to the [Opossum repository](https://github.com/majormartintibor/Opossum)
- Click "New Pull Request"
- Select your branch
- Fill out the PR template (see below)

### PR Template

```markdown
## Description
Brief description of what this PR does.

## Type of Change
- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Documentation update

## How Has This Been Tested?
- [ ] Unit tests added/updated
- [ ] Integration tests added/updated
- [ ] Manual testing performed

## Checklist
- [ ] My code follows the coding standards (see `.github/copilot-instructions.md`)
- [ ] I have used `.ConfigureAwait(false)` on all awaits
- [ ] I have added tests that prove my fix/feature works
- [ ] All existing tests pass (`dotnet test`)
- [ ] I have updated documentation if needed
- [ ] My code compiles without warnings
```

### 7. Code Review

- Maintainers will review your PR
- Address any feedback
- Once approved, your PR will be merged! 🎉

---

## Definition of Done

A contribution is complete when:

### ✅ Code Quality
- [ ] Code compiles successfully (`dotnet build`)
- [ ] No compiler warnings introduced
- [ ] Follows all coding standards (namespaces, usings, ConfigureAwait, etc.)
- [ ] Public APIs have XML documentation comments

### ✅ Testing
- [ ] **ALL existing tests pass**
  ```bash
  dotnet test tests/Opossum.UnitTests/
  dotnet test tests/Opossum.IntegrationTests/
  ```
- [ ] New unit tests added for new functionality
- [ ] New integration tests added for file system features
- [ ] Test coverage is sufficient (happy path, edge cases, error conditions)

### ✅ Documentation
- [ ] All `.md` files placed in `docs/` folder
- [ ] `Opossum.slnx` updated if docs were added
- [ ] README.md updated if public API changed
- [ ] Code comments added where necessary

### ✅ No Breaking Changes (or documented)
- [ ] Existing public APIs remain compatible
- [ ] If breaking changes are necessary, they are documented

---

## Project Structure

### Core Library (`src/Opossum/`)

```
src/Opossum/
├── Configuration/          # OpossumOptions, validation
├── Core/                   # Event, Query, QueryItem, AppendCondition
├── DependencyInjection/    # ServiceCollectionExtensions
├── Exceptions/             # Custom exceptions
├── Extensions/             # Extension methods
├── Mediator/               # Command/query mediator
├── Projections/            # Projection system
└── Storage/                # File system storage implementation
    └── FileSystem/         # Event store, ledger, indices
```

### Tests

```
tests/
├── Opossum.UnitTests/              # Pure logic tests, no I/O
│   ├── Configuration/
│   ├── Core/
│   ├── Mediator/
│   ├── Projections/
│   └── Storage/
└── Opossum.IntegrationTests/      # File system integration tests
    ├── ConcurrencyTests.cs
    ├── DescendingPerformanceTests.cs
    └── ...
```

### Sample Applications

```
Samples/
├── Opossum.Samples.CourseManagement/    # Full web API example
│   ├── CourseCreation/
│   ├── StudentRegistration/
│   ├── Projections/
│   └── Program.cs
└── Opossum.Samples.DataSeeder/          # Data generation tool
```

---

## Common Issues & Solutions

### Issue: Tests Fail Locally But CI Passes

**Solution:** Make sure you're using temporary directories in integration tests, not hardcoded paths.

### Issue: ConfigureAwait Warning (VSTHRD111)

**Solution:** Add `.ConfigureAwait(false)` to all `await` statements in `src/Opossum/`.

```csharp
❌ await stream.WriteAsync(data);
✅ await stream.WriteAsync(data).ConfigureAwait(false);
```

### Issue: Package Version Conflict

**Solution:** Update `Directory.Packages.props`, not individual `.csproj` files.

### Issue: Build Fails with "Context already exists"

**Solution:** In MVP, only add ONE context. Multiple contexts are not yet supported.

---

## Getting Help

- **Questions?** Open a [GitHub Discussion](https://github.com/majormartintibor/Opossum/discussions)
- **Bug?** Open a [GitHub Issue](https://github.com/majormartintibor/Opossum/issues)
- **Security Issue?** Email the maintainers directly (see README.md)

---

## Resources

- [DCB Specification](https://dcb.events/specification/)
- [.NET 10 Documentation](https://learn.microsoft.com/en-us/dotnet/)
- [C# Coding Conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- [ConfigureAwait FAQ](https://devblogs.microsoft.com/dotnet/configureawait-faq/)

---

**Thank you for contributing to Opossum! 🦘**

Every contribution, no matter how small, helps make the project better for everyone.
