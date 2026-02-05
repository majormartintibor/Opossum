# Opossum Specification Index

This directory contains detailed specifications for planned Opossum features. Each spec is designed to be implemented independently in its own feature branch.

---

## Specification Status

| Spec | Title | Status | Dependencies | Priority |
|------|-------|--------|--------------|----------|
| [SPEC-001](SPEC-001-Projection-Metadata.md) | Projection Metadata & Indexing | Draft | None | **P0** (Foundation) |
| [SPEC-002](SPEC-002-Cache-Warming.md) | Cache Warming | Draft | SPEC-001 | P1 |
| [SPEC-003](SPEC-003-Retention-Policies.md) | Retention Policies | Draft | SPEC-001 | P1 |
| [SPEC-004](SPEC-004-Archiving-Compression.md) | Archiving & Compression | Draft | SPEC-001, SPEC-003 | P2 |
| [SPEC-005](SPEC-005-Smart-Retention.md) | Smart Retention (Access Frequency) | Draft | SPEC-001, SPEC-003 | P3 (Future) |

---

## Implementation Order

### Phase 1: Foundation (Must be first)
1. **SPEC-001: Projection Metadata** - Required by all other features
   - Branch: `feature/projection-metadata`
   - Blocks: SPEC-002, SPEC-003, SPEC-004, SPEC-005

### Phase 2: Independent Features (Can be parallel)
2. **SPEC-002: Cache Warming** - Performance optimization
   - Branch: `feature/cache-warming`
   - Depends on: SPEC-001

3. **SPEC-003: Retention Policies** - Data lifecycle management
   - Branch: `feature/retention-policies`
   - Depends on: SPEC-001

### Phase 3: Advanced Features
4. **SPEC-004: Archiving & Compression** - Storage management
   - Branch: `feature/archiving-compression`
   - Depends on: SPEC-001, SPEC-003

### Phase 4: Future Enhancements
5. **SPEC-005: Smart Retention** - Access frequency-based retention
   - Branch: `feature/smart-retention`
   - Depends on: SPEC-001, SPEC-003
   - **Important:** Not forgotten, explicitly deferred for future implementation

---

## Dependency Graph

```
SPEC-001 (Projection Metadata)
    ├── SPEC-002 (Cache Warming)
    ├── SPEC-003 (Retention Policies)
    │       ├── SPEC-004 (Archiving & Compression)
    │       └── SPEC-005 (Smart Retention) [Future]
    ├── SPEC-004 (Archiving & Compression)
    └── SPEC-005 (Smart Retention) [Future]
```

---

## Feature Overview

### SPEC-001: Projection Metadata & Indexing

**Problem:** Projections lack lifecycle metadata (created, updated, size).

**Solution:** 
- Add `ProjectionMetadata` class
- Wrap projections with metadata on save
- Create metadata index for fast queries
- Auto-migrate existing projections

**Value:**
- Enables intelligent cache warming
- Enables retention policies
- Provides audit trail

---

### SPEC-002: Cache Warming

**Problem:** First query after startup is slow (cold cache).

**Solution:**
- Opt-in feature to pre-load recent data at startup
- Configurable time window, size budget, timeout
- IHostedService integration

**Value:**
- Predictable query performance
- Better user experience for frequently-restarted applications
- Trade startup time for query speed

---

### SPEC-003: Retention Policies

**Problem:** Projections accumulate indefinitely without lifecycle management.

**Solution:**
- Configurable per-projection retention policies
- Three modes: NoRetention, DeleteAfter, ArchiveAfter
- Tombstone pattern prevents rebuild loop
- Explicit "keep forever" vs "delete old" intent

**Value:**
- Automatic data cleanup
- Compliance with data retention regulations
- Reduced storage costs

---

### SPEC-004: Archiving & Compression

**Problem:** Old data wastes expensive fast storage.

**Solution:**
- Compress projections to ZIP format
- Move to cheaper archive storage
- Maintain tombstone with archive path
- Support restore when needed

**Value:**
- 70%+ storage reduction via compression
- Tiered storage (fast SSD → slow HDD/NAS)
- Reversible (can restore archived data)
- No external dependencies (uses System.IO.Compression)

---

### SPEC-005: Smart Retention (Access Frequency-Based)

**Problem:** Time-based retention doesn't consider usage patterns. Old frequently-accessed data gets deleted, new unused data is kept.

**Solution:**
- Track last access time (opt-in, adds minimal overhead)
- Hybrid retention: Age + inactivity threshold
- Archive projections that are old AND unused
- Keep frequently-accessed data regardless of age

**Value:**
- Smarter archival decisions based on actual usage
- Keep "hot" old data, archive "cold" new data
- Access statistics for optimization
- Better cache warming priorities

**Status:** Future enhancement (deferred after SPEC-003)

---

## Design Principles

All specifications follow these principles:

1. **Backward Compatibility:** No breaking changes to existing APIs
2. **Opt-In:** New features disabled by default
3. **Zero External Dependencies:** Only built-in .NET libraries (except test libraries)
4. **Test-Driven:** Comprehensive unit and integration test requirements
5. **Observable:** Logging at INFO level for important operations
6. **Fail-Safe:** Graceful degradation on errors (don't crash app)
7. **Well-Documented:** Each spec includes configuration examples

---

## Workflow: From Spec to Implementation

### 1. Specification Phase (Current)
- ✅ Create comprehensive spec document
- ✅ Review spec with stakeholders
- ✅ Finalize requirements and design
- ✅ Push spec to `main` branch

### 2. Implementation Phase
```bash
# Create feature branch
git checkout -b feature/projection-metadata

# Implement feature following spec
# - Create new classes
# - Modify existing classes
# - Write unit tests
# - Write integration tests

# Push to remote
git push origin feature/projection-metadata

# Create Pull Request
# - Reference spec in PR description
# - Ensure all tests pass
# - Request code review
```

### 3. Review Phase
- Code review by maintainers
- Ensure spec requirements are met
- Verify all tests passing
- Check documentation updated

### 4. Merge & Release
- Merge feature branch to `main`
- Update spec status to "Implemented"
- Tag release if appropriate
- Update CHANGELOG.md

---

## Testing Requirements

Each specification must include:

- [ ] Unit test requirements (what must be tested)
- [ ] Integration test requirements (end-to-end scenarios)
- [ ] Performance test requirements (benchmarks)
- [ ] Migration test requirements (backward compatibility)

**Definition of Done:**
- All specified tests written and passing
- Build succeeds without warnings
- Documentation updated
- Copilot-instructions followed

---

## Open for Discussion

These specs are **DRAFT** status. Feedback welcome on:

- Design decisions
- API surface
- Implementation priorities
- Testing strategies
- Documentation clarity

**How to provide feedback:**
1. Open GitHub Issue referencing spec number
2. Create discussion in GitHub Discussions
3. Comment on Pull Request when spec is merged to main

---

## Future Specifications (Not Yet Written)

Potential future specs based on discussions:

- **SPEC-006:** Command Retry Middleware
- **SPEC-007:** Event Metadata Indexing
- **SPEC-008:** Projection Snapshots (Performance)
- **SPEC-009:** Multi-Node Support (Distributed Deployment)
- **SPEC-010:** Cloud Archive Backends (S3, Azure Blob)

**Note:** SPEC-005 (Smart Retention) is fully specified and ready for implementation when time permits.

---

## Questions?

- **Slack:** #opossum-dev
- **Email:** [project maintainer email]
- **GitHub:** Open an issue or discussion

---

**Last Updated:** 2024  
**Maintained By:** Opossum Project Team
