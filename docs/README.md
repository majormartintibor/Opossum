# Opossum Documentation

This folder contains all documentation for the Opossum event store library.

## Documentation Files

- **[REFACTORING_SUMMARY.md](REFACTORING_SUMMARY.md)** - Summary of test project refactoring to remove mocking and follow best practices

## Documentation Guidelines

All documentation files (*.md) must be placed in this `docs` folder. This includes:
- Architecture documentation
- Feature specifications
- Refactoring summaries
- Design decisions
- API documentation

Never create .md files in the solution root or scattered across project folders.

## About Opossum

Opossum is a .NET library that turns your file system into an event store database. It provides features like:
- Event sourcing with file-based storage
- Projections for building read models
- Mediator pattern for command/query handling
- Dependency injection integration
- DCB (Dynamic Consistency Boundaries) specification compliance

For more information, see the main README.md at the solution root.
