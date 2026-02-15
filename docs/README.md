# Opossum Documentation Index

**Welcome to the Opossum documentation!**

This folder contains comprehensive documentation for the Opossum event store library - a .NET library that turns your file system into an event store database with DCB (Dynamic Consistency Boundaries) support.

---

## 📚 Quick Navigation

### 🚀 Getting Started
- **[Use Cases](guides/use-cases.md)** - When to use Opossum (automotive retail, offline apps, etc.)
- **[Durability Quick Reference](guides/durability-quick-reference.md)** - How to configure data persistence guarantees
- **[Projection Tags Quick Start](guides/projection-tags-quick-start.md)** - Build read models with projections
- **[ConfigureAwait Guide](guides/configureawait-guide.md)** - Understanding async/await patterns in Opossum
- **[Understanding SLNX Files](guides/understanding-slnx-files.md)** - Working with .NET 10 solution files

---

## 🏛️ Architecture & Design

### Core Concepts
- **[Architecture Overview](architecture/)** - High-level system design *(planned)*
- **[DCB Pattern](architecture/)** - Dynamic Consistency Boundaries explained *(planned)*
- **[Event Store Design](architecture/)** - File system as event store *(planned)*

### Design Decisions (ADRs)
- **[001 - ConfigureAwait Implementation](decisions/001-configureawait-implementation.md)** - Why and how we use `.ConfigureAwait(false)`
- **[ConfigureAwait Analysis](decisions/configureawait-analysis.md)** - Detailed analysis and recommendations

---

## ✨ Features

### Core Features
- **[Ledger](features/ledger.md)** - Event ledger for tracking event sequences
- **[Projection Tag Indexing](features/projection-tag-indexing.md)** - Tag-based indexing for fast queries

### Specifications
- **[DCB Specification](specifications/)** - Formal DCB spec *(to be migrated)*
- **[Event Format](specifications/)** - Event serialization format *(planned)*
- **[SPEC-001: Projection Metadata](specifications/spec-001-projection-metadata.md)** - Projection metadata specification
- **[SPEC-002: Cache Warming](specifications/spec-002-cache-warming.md)** - Cache warming strategies
- **[SPEC-003: Retention Policies](specifications/spec-003-retention-policies.md)** - Event retention policies
- **[SPEC-004: Archiving & Compression](specifications/spec-004-archiving-compression.md)** - Archiving and compression specs
- **[SPEC-005: Smart Retention](specifications/spec-005-smart-retention.md)** - Intelligent retention strategies

---

## 🔧 Implementation Details

### Performance & Optimization
- **[Durability Guarantees](implementation/durability-guarantees.md)** - How we prevent data loss on power failure
- **[Filesystem Read Optimization](implementation/filesystem-read-optimization.md)** - Performance analysis and optimizations
- **[Parallel Reads Strategy](implementation/parallel-reads-strategy.md)** - Concurrent read implementation
- **[Parallel Reads Impact](implementation/parallel-reads-impact.md)** - Performance impact summary
- **[GetAsync Lock Fix](implementation/performance-getasync-lock-fix.md)** - Performance fix for read operations

### Feature Implementations
- **[ConfigureAwait Summary](implementation/configureawait-summary.md)** - Complete implementation summary
- **[Projection Tag Indexing Summary](implementation/projection-tag-indexing-summary.md)** - Implementation details
- **[Projection Tag Indexing Progress](implementation/projection-tag-indexing-progress.md)** - Implementation progress tracking

### Phased Rollouts
- **[Phase 1-2 Summary](implementation/phase-1-2-summary.md)** - Multi-phase feature rollout
- **[Phase 2A Summary](implementation/phase-2a-summary.md)** - Specific phase details

### Bug Fixes & Improvements
- **[Enrollment Tier Limit Fix](implementation/bugfix-enrollment-tier-limit.md)** - Fixed tier limit enforcement
- **[DataSeeder Command Handler Fix](implementation/dataseeder-command-handler-fix.md)** - Fixed data seeding issues
- **[DataSeeder Fix Complete](implementation/dataseeder-fix-complete.md)** - Complete fix documentation
- **[DataSeeder Optimization](implementation/dataseeder-optimization.md)** - Performance improvements

### Testing & Quality
- **[Test Refactoring Summary](implementation/test-refactoring-summary.md)** - Removing mocks, improving test quality
- **[Test Coverage - Durability](implementation/test-coverage-durability.md)** - Durability feature test coverage

---

## ⚠️ Limitations

### MVP Restrictions
- **[Single Context Only](limitations/mvp-single-context.md)** - Multi-context support planned for future release

---

## 📁 Folder Structure

```
docs/
├── README.md                          # 👈 You are here
├── architecture/                      # System design & architecture
├── features/                          # Feature specifications
├── guides/                            # How-to guides & tutorials
├── specifications/                    # Formal specs & standards
├── implementation/                    # Implementation details & summaries
├── limitations/                       # Current MVP limitations
└── decisions/                         # Architecture Decision Records (ADRs)
```

---

## 📖 Documentation Categories Explained

| Category | Purpose | Examples |
|----------|---------|----------|
| **Architecture** | High-level system design, patterns, component interactions | DCB pattern, event store architecture |
| **Features** | User-facing functionality, API specifications | Ledger, projections, tag indexing |
| **Guides** | Step-by-step instructions, tutorials, quick starts | Getting started, how-to guides |
| **Specifications** | External specs, formal standards compliance | DCB spec, event format spec |
| **Implementation** | Technical details, algorithms, performance, bug fixes | Durability implementation, performance fixes |
| **Limitations** | Current MVP restrictions and workarounds | Single-context limitation |
| **Decisions** | Architecture Decision Records for significant choices | ConfigureAwait adoption, CPM adoption |

---

## 🎯 Finding What You Need

### I want to...
- **Understand when to use Opossum** → [Use Cases](guides/use-cases.md)
- **Learn about data persistence** → [Durability Guarantees](implementation/durability-guarantees.md)
- **Build a read model** → [Projection Tags Quick Start](guides/projection-tags-quick-start.md)
- **Understand async/await patterns** → [ConfigureAwait Guide](guides/configureawait-guide.md)
- **See performance optimizations** → [Implementation > Performance](#performance--optimization)
- **Understand design decisions** → [Decisions](#design-decisions-adrs)

---

## 🤝 Contributing to Documentation

### File Naming Rules
1. **Always use kebab-case** (lowercase with hyphens): `event-store-design.md` ✅ not `EventStoreDesign.md` ❌
2. **Be descriptive and specific**: `durability-guarantees.md` ✅ not `durability.md` ❌
3. **ADRs use sequential numbering**: `001-title.md`, `002-title.md`
4. **Implementation summaries**: `feature-name-summary.md` or `bugfix-description.md`

### Where to Place New Documents

| Document Type | Folder | Example |
|---------------|--------|---------|
| High-level design, patterns | `architecture/` | `dcb-pattern-explained.md` |
| Feature specs, APIs | `features/` | `mediator-pattern.md` |
| How-to guides, tutorials | `guides/` | `getting-started.md` |
| External specs, standards | `specifications/` | `dcb-specification.md` |
| Implementation details, fixes | `implementation/` | `performance-fix-description.md` |
| Architecture decisions | `decisions/` | `002-central-package-management.md` |

### Documentation Guidelines
- All documentation files (*.md) **MUST** be in the `docs/` folder or its subfolders
- Never create .md files in the solution root or scattered across project folders
- Keep this README.md updated when adding new major documentation
- Use clear, descriptive titles and maintain consistent formatting

---

## 📦 About Opossum

Opossum is a .NET library that turns your file system into an event store database. It provides:

- ✅ **Event sourcing** with file-based storage
- ✅ **Projections** for building read models
- ✅ **Mediator pattern** for command/query handling
- ✅ **Dependency injection** integration
- ✅ **DCB (Dynamic Consistency Boundaries)** specification compliance
- ✅ **Configurable durability** guarantees (survive power failures)
- ✅ **Optimistic concurrency** control with append conditions
- ✅ **Tag-based indexing** for fast queries

---

**For general project information, see the main README.md at the solution root.**
