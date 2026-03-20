# GDPR and Data Privacy Design Document

> **Status:** Design
> **Version:** 0.6.0
> **Dependencies:** None (can be implemented independently of throughput work)
> **Reference:** [Oskar Dudycz - GDPR in Event-Driven Architecture](https://event-driven.io/en/gdpr_in_event_driven_architecture/)

---

## Problem Statement

Opossum stores events as immutable JSON files. In regulated domains (healthcare, legal,
finance), applications must comply with GDPR's "right to erasure" (Article 17) and
general data privacy requirements. Currently, Opossum has:

- No built-in encryption
- No per-event deletion capability (only `DeleteStoreAsync` which wipes the entire store)
- No mechanism to mark events as containing personal data
- No way to selectively redact or destroy PII

For the target niche (desktop/tablet apps for practitioners, inspectors, advisors),
GDPR compliance is a **mandatory** feature, not a nice-to-have.

---

## GDPR Strategies for Event Sourcing (Analysis)

Based on the referenced article and established event sourcing literature, there are
four primary strategies. This section evaluates each for Opossum's file-per-event model.

### Strategy 1: Crypto Shredding

**Concept:** Encrypt PII fields in event payloads with a per-subject encryption key.
To "forget" a subject, delete their encryption key. The encrypted data becomes
permanently unreadable.

**Advantages for Opossum:**
- Events remain immutable on disk (no file modification needed)
- Indices and positions are unaffected
- Projections can handle "key not found" gracefully (return anonymized data)
- Backup files automatically become unreadable for deleted subjects
- File-per-event model means encryption/decryption is per-file (simple scope)
- Works perfectly with `WriteProtectEventFiles = true` (files never need to be modified)

**Implementation fit:** EXCELLENT. This is the primary recommended strategy.

**Complexity:** Medium. Requires:
- A key store (file-based, consistent with zero-infrastructure philosophy)
- Encryption/decryption hooks in the serialization layer
- A way to mark which event fields contain PII
- Handling of "key not found" during read (return anonymized/null data)

### Strategy 2: Event Replacement (Tombstone / Soft Delete)

**Concept:** Replace an event file's content with a tombstone marker while preserving
the file's position in the sequence. The tombstone contains metadata about the deletion
but no PII.

**Advantages for Opossum:**
- File-per-event makes this trivially implementable (replace one file)
- Position integrity is maintained (no gaps in the sequence)
- Indices remain valid (the position still exists)
- Projections can detect tombstones and handle gracefully
- Simple to understand and audit ("this event was deleted on date X by request Y")

**Implementation fit:** GOOD. Useful as a complementary strategy to crypto shredding.

**Complexity:** Low. Requires:
- A `SoftDeleteEventAsync(long position, string reason)` method on `IEventStoreAdmin`
- A tombstone JSON format that preserves position but removes all PII
- Projection awareness of tombstone events
- Temporary file unprotection (already exists in `AddTagsAsync`)

### Strategy 3: Forgettable Payload

**Concept:** Instead of storing PII directly in events, store a reference (URI/key) to
a separate PII store. When a subject exercises their right to erasure, delete the PII
from the separate store. Events remain intact but the linked data is gone.

**Advantages for Opossum:**
- Events are never modified
- Clean separation of concerns
- Simple deletion (just delete the PII file)

**Disadvantages for Opossum:**
- Adds complexity for users (must manage two data stores)
- The "separate PII store" would need its own encryption and access control
- Breaks the simplicity of "one file = one complete event"
- Not compatible with "open the JSON file and see the full record" value proposition
- Race conditions: if PII is cached by a reader, deletion is ineffective

**Implementation fit:** POOR for Opossum. Conflicts with the human-readable file
philosophy and adds infrastructure complexity that defeats the zero-infrastructure value.

**Recommendation:** Do NOT implement as a library feature. Users can implement this
pattern themselves if needed.

### Strategy 4: Log Compaction / Stream Truncation

**Concept:** Delete all events in a stream before a certain position, keeping only a
summary event. Used by EventStoreDB and Kafka.

**Disadvantages for Opossum:**
- Opossum uses a single global stream (DCB), not per-entity streams
- Deleting events by position would break index integrity
- Summary events would need to be "appended" at new positions, breaking history
- Complex interaction with cross-process lock and concurrent readers

**Implementation fit:** POOR. Opossum's DCB model (single global stream) makes this
strategy impractical.

**Recommendation:** Do NOT implement.

---

## Recommended Design: Crypto Shredding + Soft Delete

The recommended approach combines two strategies:

1. **Crypto Shredding** (primary) --- encrypt PII at write time, delete keys for erasure
2. **Soft Delete** (complementary) --- replace event files with tombstones when needed

### Why both?

- Crypto shredding is the **correct long-term solution** (events stay intact, keys go away)
- Soft delete is a **simple escape hatch** for cases where:
  - Events were written before encryption was enabled
  - Complete removal of the event file content is required (not just encryption key deletion)
  - Regulatory requirements demand visible deletion evidence (tombstone with audit trail)

---

## Crypto Shredding: Detailed Design

### Encryption key management

Since Opossum is zero-infrastructure, keys are stored in the file system alongside
the event store but in a **separate directory**:

```
OpossumStore/
  MyStore/
    Events/
      0000000001.json     # Contains encrypted PII fields
      0000000002.json
    Indices/
    Projections/
    Keys/                 # NEW: encryption key store
      subjects/
        {subjectId}.key   # AES-256 key for this data subject
      master.key          # Master key that encrypts subject keys (key-wrapping)
    .ledger
    .store.lock
```

### Key hierarchy

```
User-provided master password/key
       |
       v
  Master Key (derived via PBKDF2/Argon2id from password, or raw key)
       |
       v
  Subject Keys (one per data subject, AES-256, encrypted with master key)
       |
       v
  Event PII fields (encrypted with subject key)
```

**Why key-wrapping?** If the master password changes (key rotation), only the subject
key files need re-encryption, not every event file.

### PII field marking

Users mark PII fields using a C# attribute:

```csharp
public record PatientRegisteredEvent : IEvent
{
    public Guid PatientId { get; init; }

    [PersonalData]
    public string FirstName { get; init; }

    [PersonalData]
    public string LastName { get; init; }

    [PersonalData]
    public string Email { get; init; }

    [PersonalData(SubjectIdField = nameof(PatientId))]
    public string InsuranceNumber { get; init; }

    // Non-PII fields are stored in plaintext
    public string Department { get; init; }
    public DateTime RegistrationDate { get; init; }
}
```

The `[PersonalData]` attribute marks fields for encryption. The `SubjectIdField`
property on one of the attributes identifies which field contains the data subject's
identifier (used to look up the encryption key).

Alternatively, a subject ID resolver can be registered during configuration:

```csharp
services.AddOpossum(options =>
{
    options.RootPath = @"C:\AppData\MyApp";
    options.UseStore("PatientRecords");
    options.Encryption.Enable(encryptionOptions =>
    {
        encryptionOptions.MasterKey = userProvidedKey; // or derive from password
        encryptionOptions.SubjectIdResolver = (domainEvent) =>
            domainEvent.Tags.FirstOrDefault(t => t.Key == "patientId")?.Value;
    });
});
```

### Serialization integration

Encryption hooks into `JsonEventSerializer` at the field level:

**Write path (AppendAsync):**
1. Serialize event to JSON normally
2. For each `[PersonalData]` field, resolve the subject ID
3. Look up (or create) the subject's encryption key
4. Encrypt the field value with AES-256-GCM
5. Replace the plaintext value with an encrypted envelope: `{"$encrypted": "base64...", "$nonce": "base64...", "$tag": "base64..."}`
6. Write the modified JSON to the event file

**Read path (ReadAsync):**
1. Read the JSON file
2. For each encrypted field, resolve the subject ID
3. Look up the subject's encryption key
4. If key exists: decrypt and restore plaintext value
5. If key is deleted: return a sentinel value (null, `"[REDACTED]"`, or configurable)
6. Return the deserialized event

### On-disk format (encrypted event file)

```json
{
  "position": 42,
  "event": {
    "eventType": "PatientRegisteredEvent",
    "event": {
      "$type": "MyApp.Events.PatientRegisteredEvent, MyApp",
      "patientId": "a1b2c3d4-...",
      "firstName": {
        "$encrypted": "SGVsbG8gV29ybGQ=",
        "$nonce": "dW5pcXVlLW5vbmNl",
        "$tag": "YXV0aC10YWc="
      },
      "lastName": {
        "$encrypted": "SGVsbG8gV29ybGQ=",
        "$nonce": "dW5pcXVlLW5vbmNl",
        "$tag": "YXV0aC10YWc="
      },
      "email": {
        "$encrypted": "SGVsbG8gV29ybGQ=",
        "$nonce": "dW5pcXVlLW5vbmNl",
        "$tag": "YXV0aC10YWc="
      },
      "department": "Cardiology",
      "registrationDate": "2026-01-15T10:30:00Z"
    },
    "tags": [
      { "key": "patientId", "value": "a1b2c3d4-..." }
    ]
  },
  "metadata": {
    "timestamp": "2026-01-15T10:30:00Z",
    "userId": "doctor-uuid"
  }
}
```

**Note:** Tags and EventType are NEVER encrypted. They are indexing metadata required
for queries. PII should NOT be placed in tags (use opaque IDs like GUIDs instead).
This is consistent with Oskar Dudycz's advice: "NEVER put PII information like email,
user name, or insurance id into the stream name."

### GDPR erasure flow (crypto shredding)

```
1. User requests erasure for subject "patient-123"
2. Application calls: await eventStore.EraseSubjectKeyAsync("patient-123");
3. Opossum deletes: Keys/subjects/patient-123.key
4. Done. All events for "patient-123" now have unreadable PII fields.
5. Projections that read these events get null/REDACTED for PII fields.
6. Application rebuilds affected projections (optional but recommended).
```

### Encryption algorithms

- **Field encryption:** AES-256-GCM (authenticated encryption, built into .NET via
  `System.Security.Cryptography.AesGcm`)
- **Key wrapping:** AES-256-KWP (key wrap with padding) or AES-256-GCM encrypting
  the raw subject key bytes
- **Key derivation from password:** Argon2id (via .NET 10's built-in support) or
  PBKDF2-SHA512 (fallback)
- **All algorithms are from `System.Security.Cryptography`** --- no external dependencies

### Key rotation

The master key can be rotated without touching event files:

1. Decrypt all subject keys with old master key
2. Re-encrypt all subject keys with new master key
3. Overwrite subject key files atomically (temp + move pattern)

This is a `O(subjects)` operation, not `O(events)`. For a desktop app with hundreds
of subjects, this completes in milliseconds.

---

## Soft Delete: Detailed Design

### API

```csharp
public interface IEventStoreAdmin
{
    // Existing
    Task DeleteStoreAsync(CancellationToken cancellationToken = default);

    // NEW: Replace a single event with a tombstone
    Task SoftDeleteEventAsync(
        long position,
        string reason,
        CancellationToken cancellationToken = default);

    // NEW: Replace all events matching a query with tombstones
    Task SoftDeleteEventsAsync(
        Query query,
        string reason,
        CancellationToken cancellationToken = default);
}
```

### Tombstone format

When an event at position 42 is soft-deleted, the file `Events/0000000042.json`
is replaced with:

```json
{
  "position": 42,
  "event": {
    "eventType": "$tombstone",
    "event": {
      "$type": "Opossum.Core.TombstoneEvent, Opossum",
      "originalEventType": "PatientRegisteredEvent",
      "reason": "GDPR erasure request #REQ-2026-001",
      "deletedAt": "2026-03-15T14:22:00Z",
      "deletedBy": "system"
    },
    "tags": []
  },
  "metadata": {
    "timestamp": "2026-01-15T10:30:00Z"
  }
}
```

### Behavior

- The position is preserved (no gaps in the sequence)
- The `$tombstone` event type is a reserved type that Opossum never returns from
  `ReadAsync` (filtered out by default)
- Indices are NOT updated (the old position entries remain but point to a tombstone;
  filtered on read)
- Projections encountering a tombstone skip it (no `Apply` call)
- `ReadAsync` with a special `ReadOption.IncludeTombstones` flag returns them (for
  audit purposes)

### Write protection handling

If `WriteProtectEventFiles = true`, soft delete:
1. Removes the read-only attribute
2. Writes the tombstone via the standard temp-file + move pattern
3. Re-applies the read-only attribute

This pattern already exists in `AddTagsAsync`.

---

## Configuration

```csharp
services.AddOpossum(options =>
{
    options.RootPath = @"C:\AppData\MyApp";
    options.UseStore("PatientRecords");

    // Enable encryption (optional --- store works without it)
    options.Encryption.Enable(enc =>
    {
        enc.MasterKey = Convert.FromBase64String(config["EncryptionKey"]);
        // OR derive from password:
        // enc.DeriveFromPassword(userPassword, salt);

        enc.SubjectIdResolver = (domainEvent) =>
            domainEvent.Tags.FirstOrDefault(t => t.Key == "patientId")?.Value;

        enc.RedactedValue = "[REDACTED]"; // default
    });
});
```

### Encryption is opt-in and non-breaking

- Stores created without encryption continue to work unchanged
- Encryption can be enabled on an existing store (new events get encrypted, old ones don't)
- Reading old unencrypted events when encryption is enabled works (no `$encrypted` marker = plaintext)
- This allows gradual migration without downtime

---

## Implementation Phases

### Phase 1: Soft Delete (Low effort, immediate value)

- `TombstoneEvent` record type
- `SoftDeleteEventAsync` on `IEventStoreAdmin`
- `SoftDeleteEventsAsync` on `IEventStoreAdmin`
- `ReadOption.IncludeTombstones`
- Tombstone filtering in `ReadAsync` and `ReadLastAsync`
- Projection tombstone awareness
- Unit tests + integration tests

### Phase 2: Encryption Infrastructure (Medium effort, foundational)

- `[PersonalData]` attribute
- `EncryptionOptions` configuration
- `SubjectKeyStore` (file-based key management)
- `MasterKeyProvider` (key derivation + key wrapping)
- AES-256-GCM encrypt/decrypt utilities
- Unit tests for crypto operations

### Phase 3: Serialization Integration (Medium effort, core feature)

- `EncryptingJsonEventSerializer` (wraps `JsonEventSerializer`)
- Write-path encryption (per-field, based on `[PersonalData]`)
- Read-path decryption (with key-not-found handling)
- Subject key auto-creation on first encrypt
- Integration tests with real event store

### Phase 4: GDPR Erasure API (Low effort, user-facing)

- `EraseSubjectKeyAsync(string subjectId)` on `IEventStoreAdmin`
- `GetSubjectIdsAsync()` for listing known subjects
- Key rotation API (`RotateMasterKeyAsync`)
- Integration tests for full erasure flow
- Projection rebuild after erasure (documentation + helper)

---

## What This Enables

With crypto shredding + soft delete, Opossum can credibly serve:

| Use case | GDPR requirement | How Opossum handles it |
|----------|-----------------|----------------------|
| Patient requests data deletion | Right to erasure (Art. 17) | Crypto shredding: delete patient's encryption key |
| Auditor inspects records | Lawful basis for processing | Human-readable JSON files (non-PII fields remain visible) |
| Data breach notification | Breach assessment (Art. 33) | Encrypted PII = no breach for those fields |
| Data portability request | Right to portability (Art. 20) | Export store as zip (already possible) |
| Consent withdrawal | Right to withdraw (Art. 7) | Soft delete: replace events with tombstones |
| Regulator requests audit | Accountability (Art. 5) | Tombstones provide deletion audit trail |

---

## Open Questions

1. **Should tags be encryptable?** Current recommendation is NO (tags are indexing
   metadata; use opaque IDs). But some users may want to encrypt tag values.
   **Decision:** Defer. Tags remain plaintext in 0.6.0. Document that PII must not be
   placed in tag values.

2. **Should projections auto-rebuild after key deletion?** Current recommendation is
   to provide a helper but not auto-rebuild. The user knows which projections are
   affected.
   **Decision:** Provide `RebuildProjectionsForSubjectAsync(subjectId)` as a convenience.

3. **Key file format?** JSON with encrypted key bytes + metadata (algorithm, creation date,
   subject ID). Allows future extension without format change.
   **Decision:** JSON envelope with base64-encoded encrypted key bytes.

4. **What about existing events when encryption is first enabled?** They remain
   unencrypted. The serializer handles both formats transparently.
   **Decision:** Accepted. Document this as expected behavior. Provide an optional
   migration utility to retroactively encrypt existing events in a future release.
