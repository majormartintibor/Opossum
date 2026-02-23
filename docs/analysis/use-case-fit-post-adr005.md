# Opossum Use-Case Fit: Post ADR-005 + Throughput Improvements

> **Purpose:** Objective analysis of where Opossum becomes genuinely useful after
> implementing cross-process safety (ADR-005) and the throughput improvements (A+B+E).
> Based on capability analysis cross-referenced against real market needs, regulatory
> requirements, and deployment realities.
>
> **Scope:** This document does not repeat the README. It starts from the actual
> capability profile and reasons outward to real use cases.

---

## Part 1 — What the Capability Profile Actually Is

After ADR-005 + A+B+E, the honest capability statement is:

| Capability | Reality |
|---|---|
| **Concurrent writers** | Multiple OS processes, any machine on a Windows SMB share |
| **Throughput** | ~185 events/sec with full durability (fsync=true) |
| **Concurrency model** | DCB — cross-stream optimistic concurrency, not just per-stream |
| **Projection engine** | Polling daemon, auto-rebuild, tag-indexed filtering |
| **Storage format** | Human-readable JSON files in a directory tree |
| **Infrastructure required** | A shared folder. Nothing else. |
| **Backup/restore** | Copy the folder. Restore by copying it back. |
| **Data location** | Always on the machine that owns the folder. Never transmitted. |
| **Access control** | NTFS filesystem permissions. Nothing built into Opossum. |
| **Encryption** | OS-level (BitLocker, EFS). Nothing built into Opossum. |
| **Schema evolution** | Not implemented. Breaking event changes require manual migration. |
| **Read model** | Arrays loaded fully into memory. No streaming. |
| **Event delivery** | Polling only. No push subscriptions. |

The multi-process unlock is the single biggest change. Before ADR-005, Opossum was
functionally a single-user library regardless of marketing claims. After ADR-005, it
is a genuine multi-user shared data store for SMB-scale deployments.

---

## Part 2 — The Market Gap

### What small organisations actually use today for record-keeping

The target audience for Opossum is organisations with 5–100 employees that need
structured, auditable data records. What they actually use:

| Solution | Prevalence | Problem |
|---|---|---|
| **Excel / shared spreadsheet** | Very high | Not immutable, concurrent edit conflicts, no audit trail |
| **Microsoft Access** | High (legacy) | Not immutable, mutable history, poor concurrent access, dying platform |
| **Bespoke SQL Server app** | Medium | Requires DBA or consultant, UPDATE/DELETE destroy history |
| **SAP Business One** | SMB tier, €800–2,000/month | Expensive, over-engineered, requires implementation partner |
| **Cloud SaaS** | Growing | Monthly cost, data sovereignty concerns, requires internet |
| **Paper records** | Still common in regulated industries | Cannot query, easy to lose or alter |

**The gap Opossum fills:** An application backend that is cheaper to deploy than any
SaaS, simpler to operate than SQL Server, genuinely immutable unlike Excel or Access,
and capable of enforcing business invariants (DCB) across multiple concurrent users.

### The infrastructure Opossum can use for free

Approximately 80% of SMBs already run Windows with a file server or NAS device for
shared document storage. This infrastructure is already purchased, already maintained,
and already backed up. Opossum turns it into a multi-user event store with no
additional installation.

A Windows NAS (Synology, QNAP, Windows Server) can serve the Opossum store directory
to 10–15 concurrent PCs on a LAN with no configuration beyond a shared folder
permission.

---

## Part 3 — Use Cases Where Opossum Is Genuinely the Best Tool

The following cases are selected based on three criteria:
1. The regulatory or business requirement for immutable, auditable records is real
   and mandatory — not optional
2. The organisation scale is right (2–20 concurrent users, low event rate)
3. Opossum's specific capability profile (file-based, zero-infra, DCB) provides
   something that cheaper alternatives cannot

---

### Use Case 1: ISO 9001 Quality Management at a Small Manufacturer

**The regulatory driver:**  
ISO 9001:2015 clause 7.5 mandates documented information that must be controlled and
retained as evidence of conformity. IATF 16949 (automotive supply chain) imposes the
same with more rigour. External auditors periodically review these records.

**The operational reality:**  
A 50-person manufacturer with ISO certification typically has 4–8 people creating
quality records: production supervisors, QC inspectors, warehouse staff. They work on
the factory floor on Windows PCs or tablets. Records include: batch creation, inspection
results, non-conformance events, corrective actions, customer complaints.

**Why Opossum fits:**
- Append-only model satisfies "records shall not be altered" (ISO 9001:2015 7.5.3.2)
- Human-readable JSON means an external ISO auditor can examine records without
  special software — a genuine practical advantage at audit time
- 4–8 concurrent writers on a shared folder is exactly what ADR-005 enables
- DCB prevents duplicate batch numbers, duplicate serial numbers, conflicting
  inspection results for the same part
- Projection daemon generates the summary reports auditors ask for
- ~1–4 events/sec for a typical 50-person factory: Opossum has ~45× headroom

**Event rate calculation:**  
50 production orders/day × 8 events each = 400 events/day = 0.005 events/sec average.
Peak burst during shift handover: ~30 events in 5 minutes = 0.1 events/sec. The
185 events/sec ceiling is never approached.

**What they would use instead:**  
A bespoke SQL Server application (mutable, no audit trail built in) or paper records
(not queryable). SAP Quality Management starts at €30,000 per implementation.

---

### Use Case 2: Multi-Terminal Compliance-Tracked Retail (Not Generic POS)

This is distinct from the README's generic POS mention. The specific fit is retail
environments with **regulatory traceability requirements**.

**Examples:** Pharmacy dispensing records (every dispensing event must be logged per
EU Directive 2001/83/EC and national pharmacy law), veterinary medicine dispensing,
controlled substance tracking.

**Why this is different from a generic POS:**  
Traceability is legally mandatory, not optional. Records cannot be amended, only
supplemented. The pharmacist at terminal 1 and the pharmacy technician at terminal 2
must both write to the same immutable log. A customer asking "what did you dispense to
me in March?" requires replaying the event history.

**Why Opossum fits:**
- DCB prevents two terminals from dispensing the last unit of a controlled substance
  simultaneously (classic concurrency problem in pharmacy)
- Append-only store satisfies the "records cannot be amended" legal requirement
- 2–5 terminals in a small pharmacy: typical scenario is 0.01–0.5 events/sec
- Human-readable JSON: a pharmacist inspector can verify records without a developer

**What they would use instead:**  
Dedicated pharmacy software (expensive, locked vendor), or an SQL database where
nothing stops an UPDATE statement from altering a historical dispensing record.

---

### Use Case 3: Regulated Laboratory Record-Keeping (GLP / GMP)

**The regulatory driver:**  
Good Laboratory Practice (OECD GLP Principles) and Good Manufacturing Practice
(EU GMP Annex 11) require that computerised systems maintain complete, accurate,
and indelible records of all activities. Annex 11 section 4.8: audit trails that
record all entries and changes with date, time, and operator identity.

**The operational reality:**  
A contract testing laboratory (5–30 staff) receives samples, performs tests, and
records results. Analysts work at multiple workstations. Each test generates a series
of events: sample received, test initiated, interim results, final result, reviewed,
approved. Any retrospective query ("what was the instrument state when sample X was
tested?") must be answerable from the audit log.

**Why Opossum fits:**
- Audit trail is a structural property, not a bolt-on: events are immutable by design
- DCB enforces business rules: a sample cannot be marked "approved" unless a
  "reviewed" event already exists for it (enforced by AppendCondition)
- Projection daemon generates the sample status dashboard at all times
- Shared NAS folder: all workstations write to the same store
- EU GMP Annex 11 section 7.1 requires "physical and/or logical controls" to restrict
  changes. NTFS permissions on the folder provide the physical control; Opossum's
  append-only model provides the logical control.

**Why the human-readable JSON format matters here specifically:**  
GMP inspectors from the MHRA or EMA can request records in a format they can read.
A ZIP of JSON files satisfies this. A PostgreSQL dump does not.

---

### Use Case 4: Multi-User Professional Services with Billing Audit Requirements

**Tax accounting firms, legal practices, HR consultancies (5–20 staff).**

**The regulatory/business driver:**  
Fee earners create time records, billing events, and client matter events. Tax law in
most jurisdictions requires retention of billing records for 7–10 years. These records
must be verifiable as authentic (a backdated invoice is a criminal matter). Multiple
staff members work concurrently on the same client matters.

**Why Opossum fits:**
- Immutable event log: a `TimeRecorded` event with a timestamp cannot be altered after
  the fact. This matters legally.
- DCB prevents double-billing: AppendCondition ensures that a `InvoiceIssued` event
  cannot be appended if a conflicting invoice already exists for the same work
- 5–20 concurrent fee earners on a shared folder: exactly ADR-005's target range
- Projection daemon generates the "matter history" view and billing reports
- Event rate: a busy 15-person firm might record 100–300 time entries per day =
  0.003 events/sec. The ceiling is irrelevant.

**The specific advantage over a SQL database:**  
A junior staff member with database access can UPDATE a time record in SQL. In
Opossum, there is no UPDATE. A correction requires a `TimeRecordAmended` event that
permanently records what was changed, when, and by whom. This is legally meaningful.

---

### Use Case 5: HACCP-Compliant Food Production Records

**The regulatory driver:**  
EU Regulation 852/2004 (food hygiene) mandates HACCP (Hazard Analysis and Critical
Control Points) with documented records of monitoring activities. EU Regulation 178/2002
mandates full traceability: "one step back, one step forward" for all food businesses.

**The operational reality:**  
A small food producer (bakery, cheesemaker, meat processor, 10–50 employees) must
record: batch receipt of raw materials, temperature monitoring at critical control
points, production batch creation, shelf-life labelling, distribution records. These
records must be retained for the shelf life of the product plus a defined period.

**Why Opossum fits:**
- Traceability query: "which production batches used raw material batch X, and where
  were they distributed?" — this is exactly what tag-based querying solves
- Immutability: food safety inspectors (EHO, FSA) verify that records cannot be
  altered after the fact
- 2–6 production staff writing records during a shift: ADR-005's target range
- Event rate for a small producer: ~50–200 events/day (batch events, CCP readings
  triggered by threshold alerts, not continuous telemetry) = negligible rate
- Projection daemon: generates the daily HACCP log for the production manager

**The critical distinction from IoT/sensor telemetry:**  
HACCP records are triggered events ("temperature exceeded threshold at CCP-2", not
"temperature is 4.1°C" every 30 seconds). The distinction is important: 
telemetry is time-series data; HACCP events are business events. Opossum is for the
latter.

---

### Use Case 6: Developer Tooling and Internal Audit Infrastructure

This is not a vertical market — it is a developer-tool use case that the README
underweights but that is legitimately strong.

**The specific fit:** Internal tools, admin panels, and audit infrastructure where
a developer needs an event store for a service that runs on a single machine or within
a team's shared development environment, without any external dependencies.

Examples:
- A deployment audit tool that records every deployment event, configuration change,
  and rollback across a team's servers
- An internal admin panel for a SaaS product's operations team where each
  administrator action is an immutable event
- A CI/CD pipeline's state machine where each job state transition is an event

**Why Opossum is specifically good here:**
- `dotnet add package Opossum` and configure a path: that is the entire setup
- No Docker Compose, no PostgreSQL container, no EventStoreDB binary in CI
- The event store works in a GitHub Actions runner, a local dev machine, and a
  production server identically
- Human-readable JSON: when something goes wrong, the developer opens the folder and
  reads what happened

**The honest competitor here:**  
SQLite. But SQLite does not give you a projection framework, DCB, or event sourcing
semantics. Opossum gives all three for the same zero-infrastructure cost.

---

## Part 4 — What Changes Most After ADR-005

### The single-user → multi-user transition

Before ADR-005, every use case above required one application instance to be the sole
writer. This meant:

- A quality management app could only be used from one PC at a time (physically
  impossible on a factory floor)
- A pharmacy dispensing system needed a central server application that all terminals
  connected to — which is exactly the "server to manage" problem Opossum is supposed
  to avoid
- A legal billing app could only be open on one machine at a time

After ADR-005, all of these become genuinely multi-user. Each PC runs its own instance
of the application. All instances share one store directory on the network. The
`.store.lock` file serialises appends transparently. From the application's perspective
and the user's perspective, this is exactly like using a shared folder — which is
infrastructure every SMB already has and understands.

### DCB in multi-user context

DCB's value is proportional to the number of concurrent writers. With a single writer,
optimistic concurrency is academic. With 4–8 concurrent writers all working on related
records, it becomes operationally necessary.

The use cases above all have concrete examples of DCB-prevented bugs:
- Quality management: two inspectors trying to close the same batch simultaneously
- Pharmacy: two terminals trying to dispense the last unit of stock
- Legal billing: two fee earners trying to mark the same matter as closed

These are not theoretical. They are the actual bugs that appear in the SQL applications
these organisations currently use, filed as "sometimes we get duplicate records" or
"occasionally two people edit the same thing at the same time."

---

## Part 5 — What Still Excludes Opossum

This is not a complete list of limitations — those are in the competitive analysis doc.
These are the specific exclusion criteria relevant to the use cases above.

### Geographic distribution of writers

All five use cases above assume writers are on the same LAN accessing the same SMB
share. If two offices in different cities both need to write to the same store, SMB
over WAN is too slow (100–300 ms round-trip vs 1–4 ms on LAN) and file locking over
WAN is unreliable.

Opossum has no replication or multi-site sync. A multi-location chain (3+ sites) that
needs consolidated records cannot use a single Opossum store across sites.

**Realistic boundary:** One physical location, one LAN. Multiple buildings on a campus
connected by fast fibre: potentially viable. WAN-separated offices: not viable.

### Access control and data security

None of the use cases above have Opossum providing access control or encryption.
NTFS permissions restrict who can write to the folder. BitLocker encrypts the drive.
These are OS-level controls that the IT environment must configure independently.

For HIPAA, GMP Annex 11, and similar regulations, the application must be able to
demonstrate that only authorised users performed specific actions. This requires the
application layer to record user identity in event metadata — which Opossum supports
via `Metadata.UserId` — but the enforcement of "only authorised users can call
AppendAsync" is outside Opossum's scope.

### Schema evolution

Every use case above involves a production application that will be maintained over
years. When `InspectionResultRecorded` gains a new required field in v2, there is no
upcasting pipeline. This is a real maintenance burden that must be planned for at
application design time. Versioned event type names (e.g., `InspectionResultRecorded_v2`)
as a convention is the only current workaround, and it is not built-in.

---

## Part 6 — Summary

| Use Case | Concurrent users | Events/sec | ADR-005 required | DCB value | Compliance driver |
|---|---|---|---|---|---|
| ISO 9001 quality management | 4–8 | < 0.1 | ✅ Yes | Duplicate serial/batch | ISO 9001:2015 7.5 |
| Pharmacy/controlled substance dispensing | 2–5 | < 0.5 | ✅ Yes | Last-unit race | EU Directive 2001/83/EC |
| GLP/GMP laboratory records | 3–8 | < 0.1 | ✅ Yes | Approval workflow | EU GMP Annex 11 |
| Professional services billing | 5–20 | < 0.01 | ✅ Yes | Double-billing | Tax law (jurisdiction-dependent) |
| HACCP food production records | 2–6 | < 0.05 | ✅ Yes | Batch traceability | EU Regulation 178/2002 |
| Developer tooling / internal audit | 1–5 | < 1 | Optional | Deployment conflicts | Internal |

**Every single use case in this table:**
- Has an actual regulatory or legal requirement for immutable records
- Operates at event rates that are 10–100× below Opossum's post-A+B+E ceiling
- Has a Windows file server or NAS already deployed
- Cannot justify or does not want to manage a database server
- Benefits from human-readable JSON files at audit time

This is not a coincidence. The overlap between "mandatory immutable records" and
"small organisations without DBA infrastructure" defines Opossum's realistic market.
The README calls it "compliance-heavy industries" — the reality is more specific:
small organisations in regulated industries where the regulatory body will physically
examine records and where the IT environment is a Windows file share.
