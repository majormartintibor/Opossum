# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0-preview.1] - 2025-02-11

### 🎉 First Preview Release

This is the first preview release of Opossum - a file system-based event store implementing the DCB (Dynamic Consistency Boundaries) specification.

### ✨ Features

#### Core Event Store
- **File-based storage** - Events stored as JSON files in structured directories
- **DCB implementation** - Full support for Dynamic Consistency Boundaries specification
- **Optimistic concurrency** - Append conditions for race-free operations
- **Tag-based indexing** - Fast event queries without full scans
- **Event Type indexing** - Efficient filtering by event type
- **Ledger system** - Monotonic sequence positions
- **Durability guarantees** - Configurable flush-to-disk behavior

#### Projection System
- **Materialized views** - Rebuild read models from events
- **Tag-based projection queries** - Fast projection lookups
- **Multi-stream projections** - Query related events across streams
- **Automatic rebuilding** - Rebuild projections from scratch
- **Assembly scanning** - Auto-discover projection definitions

#### Developer Experience
- **.NET 10 support** - Built for latest .NET
- **Dependency injection** - First-class DI support
- **ConfigureAwait(false)** - Library-safe async/await
- **Mediator pattern** - Built-in command/query handling
- **Fluent API** - Intuitive event building with extensions
- **Sample application** - Complete course management example

#### Configuration
- **Flexible configuration** - appsettings.json binding support
- **Platform-aware paths** - Handles Windows/Linux path differences
- **Validation** - Built-in options validation

### 📝 Documentation
- Comprehensive README with quick start guide
- API reference documentation
- Sample application demonstrating real-world usage
- CONTRIBUTING guide for contributors
- Use case documentation (automotive retail, POS systems, etc.)
- Performance characteristics and scalability limits

### ⚠️ Known Limitations (MVP)
- **Single context only** - Multi-context support planned for future release (see `docs/limitations/mvp-single-context.md`)
- **No cache warming** - Feature planned but not in preview
- **Single-server deployments** - Not designed for distributed systems
- **File count limits** - Performance degrades beyond ~10M events

### 🎯 Target Use Cases
- On-premises applications
- Offline-first applications
- Small business ERP/POS systems
- Development & testing environments
- Compliance-heavy industries requiring audit trails
- Budget-conscious deployments avoiding cloud costs

### 📦 Package Information
- **Package ID:** Opossum
- **Target Framework:** .NET 10.0
- **License:** MIT
- **Repository:** https://github.com/majormartintibor/Opossum

### 🚀 Getting Started

```bash
dotnet add package Opossum --version 0.1.0-preview.1
```

See [README.md](README.md) for complete quick start guide.

### 🙏 Acknowledgments
- Inspired by the [DCB Specification](https://dcb.events/)
- Built for real-world use cases in automotive retail and SMB applications

---

## [Unreleased]

### Planned Features
- Multi-context support
- Cache warming for projections
- Snapshot support for aggregates
- Event schema versioning
- Retention policies
- Archiving and compression
- Cross-platform performance optimizations

[0.1.0-preview.1]: https://github.com/majormartintibor/Opossum/releases/tag/v0.1.0-preview.1
