## Description

<!-- Briefly describe the changes in this PR -->

## Related Issue/Spec

<!-- Link to related issue or specification document -->
- Closes #(issue number)
- Implements: [SPEC-XXX](link to spec)

## Type of Change

<!-- Mark the appropriate option with an 'x' -->

- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Documentation update
- [ ] Refactoring (no functional changes)

## Checklist

<!-- Ensure all items are completed before requesting review -->

### Code Quality

- [ ] Code follows copilot-instructions rules (namespaces, usings, etc.)
- [ ] Code compiles without warnings
- [ ] No breaking changes to existing APIs (or explicitly documented)

### Testing

- [ ] ✅ **All unit tests pass** (`dotnet test tests/Opossum.UnitTests/`)
- [ ] ✅ **All integration tests pass** (`dotnet test tests/Opossum.IntegrationTests/`)
- [ ] New unit tests added for new functionality
- [ ] New integration tests added for new features
- [ ] Test coverage is sufficient (edge cases, error conditions, happy paths)

### Documentation

- [ ] All .md files placed in `docs/` folder
- [ ] Code comments updated (if needed)
- [ ] README updated (if needed)
- [ ] Specification document updated (if implementing a spec)

### Pre-Completion Verification

```
✅ Build successful: dotnet build
✅ Unit tests passing: dotnet test tests/Opossum.UnitTests/
✅ Integration tests passing: dotnet test tests/Opossum.IntegrationTests/
✅ Sample app runs without errors (if applicable)
✅ No breaking changes to existing APIs
✅ All documentation updated
✅ Copilot-instructions followed
```

## Additional Notes

<!-- Any additional information reviewers should know -->

---

**I confirm that this PR is ready for review and meets all Definition of Done criteria.**
