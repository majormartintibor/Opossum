# Feasibility Analysis: "Opt-In Token" DCB Example in Opossum

**Source:** https://dcb.events/examples/opt-in-token/  
**Date:** 2025  
**Scope:** Can the DCB "Opt-In Token" example be implemented in `Opossum.Samples.CourseManagement`
using the current state of the Opossum library?

---

## 1. What the DCB Example Demonstrates

The "Opt-In Token" example showcases a specific capability of DCB:
**it can replace an entire read model for token validation scenarios**.

### The Pattern

A two-step flow involving server-generated, single-use tokens:

1. **Issue** — the server generates a token and stores it as a domain event tagged with the
   token value. No separate "valid tokens" table or projection is created.
2. **Redeem** — when the client submits the token, the server validates it entirely through
   the event store s DCB query layer:
   - Projection A: was this token ever issued? (`bool`)
   - Projection B: was this token already redeemed? (`bool`)
   - If issued AND not yet redeemed -> append a redemption event and take the action.

### The Key Insight: DCB as a Read-Model Replacement

Traditional approaches maintain a persistent "valid tokens" read model/table that tracks
whether each token is active. With DCB, **no such read model is needed**. The event store
query itself, scoped to the specific token tag, is the validation. The decision projection
is ephemeral -- it reads from the event log, makes the decision, and is discarded. Nothing
is persisted separately for token state.

This is distinct from the "Prevent Record Duplication" pattern in two important ways:

| | Prevent Record Duplication | Opt-In Token |
|---|---|---|
| Token origin | **Client-generated** (for idempotency) | **Server-generated** (domain-meaningful permission) |
| Flow structure | Single command with embedded token | **Two separate commands**: Issue + Redeem |
| Domain meaning | Infrastructure concern only | The token IS a domain entity |
| Decision model at redeem | Not applicable | 2 projections: WasIssued + WasAlreadyRedeemed |
| Token reuse after cancellation | Out-of-scope | Core part of the pattern |

---

## 2. Domain Adaptation: Exam Registration Token

### Domain Concept Selection

Three natural fits were considered:

| Option | Domain concept | Notes |
|---|---|---|
| **A** | **Exam Registration Token** | Instructor controls access to exam slots; optional seat capacity adds a second projection dimension |
| B | Completion Certificate Token | Simplest fit -- no capacity concern |
| C | Assignment Resubmission Token | Instructor grants late/redo permission |

**Exam Registration Token** was chosen because it is the richest while remaining completely
independent of the existing CourseEnrollment feature.

### Mapping

| DCB Example | Opossum Adaptation |
|---|---|
| Opt-in subscription token | Exam registration token |
| Token issued by system | Token issued by instructor for a specific exam |
| Token redeemed by user | Token redeemed by student to register for the exam |
| OptInTokenCreatedEvent | ExamRegistrationTokenIssuedEvent |
| OptInConfirmedEvent | ExamRegistrationTokenRedeemedEvent |
| token:{value} tag | examToken:{tokenId} tag |

### Why This Domain Fits

1. **Server-generated**: Instructor decides who may sit the exam.
2. **Domain-meaningful**: The token IS the seat allocation.
3. **Two-step flow**: Issue (instructor) -> Redeem (student). Separate, time-separated requests.
4. **Single-use**: Once redeemed, cannot be used again.
5. **Natural revocation**: Exam rescheduled -> instructor revokes outstanding tokens.
6. **Optional seat capacity**: Adds a third ExamSeatAvailable projection -- demonstrates composition.
7. **Clean separation**: Entirely independent of course enrollment.

---

## 3. Mapping to Opossum Primitives

### 3.1 Issue Command -- Unconditional Append

Issuing a token is a simple unconditional append of ExamRegistrationTokenIssuedEvent.
No decision model is needed. The TokenId (server-assigned Guid) is returned to the instructor.

Optional guard: CourseExists(courseId) projection prevents issuing tokens for non-existent courses.

### 3.2 Redeem Command -- Two-Projection Decision Model

```csharp
var (wasIssued, wasRedeemed, appendCondition) =
    await eventStore.BuildDecisionModelAsync(
        ExamRegistrationTokenProjections.TokenWasIssued(command.TokenId),
        ExamRegistrationTokenProjections.TokenWasRedeemed(command.TokenId));

if (!wasIssued)
    return CommandResult.Fail("Exam registration token not found.");

if (wasRedeemed)
    return CommandResult.Fail("Exam registration token has already been used.");
```

Uses the existing BuildDecisionModelAsync<T1, T2> overload.

### 3.3 Tags

Both issue and redeem events carry the examToken:{tokenId} tag. Both projections query
exclusively by this tag -- the token tag is the entire consistency boundary.

Issue event tags: examToken:{tokenId}, examId:{examId}, courseId:{courseId}
Redeem event tags: examToken:{tokenId}, examId:{examId}, studentId:{studentId}

The examId and courseId tags are secondary -- for read models, not for the validation boundary.

### 3.4 No Persistent Read Model for Token State -- DCB Replaces It

Traditional: TokenId -> { Status, ExamId, IssuedAt, ... } read model/table.
With DCB: no such read model needed. The event store IS the token registry.

### 3.5 AppendCondition Scoping

BuildDecisionModelAsync scopes the AppendCondition to the union of both token queries.
Different tokens never contend. Two concurrent redemptions of the same token: one succeeds,
the second reads wasRedeemed = true on retry.

### 3.6 Retry

The redeem handler wraps in ExecuteDecisionAsync for automatic retry.

---

## 4. Gap Analysis

### 4.1 No Blocking Gaps

| Requirement | Opossum Primitive | Status |
|---|---|---|
| Two-projection decision model | BuildDecisionModelAsync<T1, T2> | OK |
| Token-scoped tag query | QueryItem.Tags + Tag("examToken", id) | OK |
| Unconditional append for Issue | AppendAsync(events, null) | OK |
| AppendCondition scoped to token | Auto-generated by BuildDecisionModelAsync | OK |
| Retry on conflict | ExecuteDecisionAsync | OK |
| Optional read-side token list | IProjectionDefinition<TState> | OK |
| Optional seat capacity projection | BuildDecisionModelAsync<T1, T2, T3> | OK |

### 4.2 Optional: Revocation Extension

Requires ExamRegistrationTokenRevokedEvent tagged examToken:{tokenId}.
Replace two bool projections with a single ExamTokenStatus enum: NotIssued | Issued | Revoked | Redeemed.
Fully supported by existing API.

### 4.3 Optional: Exam Seat Capacity

A third SeatsAvailable(examId) projection can be added using the 3-projection overload.
Out of scope for base implementation.

---

## 5. Maturity Verdict

**Opossum is fully capable today. No library changes required.**

The pattern is a direct application of BuildDecisionModelAsync<T1, T2> with two tag-scoped
projections. Simpler than the already-implemented Course Announcement feature.

---

## 6. Implementation Plan

### 6.1 File Structure

```
Samples/Opossum.Samples.CourseManagement/
  Events/
    ExamRegistrationTokenIssuedEvent.cs
    ExamRegistrationTokenRedeemedEvent.cs
    ExamRegistrationTokenRevokedEvent.cs
  ExamRegistration/
    ExamRegistrationTokenProjections.cs
    IssueExamRegistrationToken.cs
    RedeemExamRegistrationToken.cs
    RevokeExamRegistrationToken.cs
```

### 6.2 Events

ExamRegistrationTokenIssuedEvent(TokenId, ExamId, CourseId)
  Tags: examToken:{tokenId}, examId:{examId}, courseId:{courseId}

ExamRegistrationTokenRedeemedEvent(TokenId, ExamId, StudentId)
  Tags: examToken:{tokenId}, examId:{examId}, studentId:{studentId}

ExamRegistrationTokenRevokedEvent(TokenId, ExamId)
  Tags: examToken:{tokenId}, examId:{examId}

### 6.3 Decision Projections

Base -- two bool projections:

  TokenWasIssued(tokenId) -> bool
    Query: ExamRegistrationTokenIssuedEvent, tag examToken:{tokenId}
    Apply: (_, _) => true
    Initial: false
    Guard: if (!wasIssued) -> "Exam registration token not found."

  TokenWasRedeemed(tokenId) -> bool
    Query: ExamRegistrationTokenRedeemedEvent, tag examToken:{tokenId}
    Apply: (_, _) => true
    Initial: false
    Guard: if (wasRedeemed) -> "Exam registration token has already been used."

With revocation -- single ExamTokenStatus enum projection:

  ExamTokenStatus(tokenId) -> enum { NotIssued | Issued | Revoked | Redeemed }
    Query: all three event types, tag examToken:{tokenId}
    Apply:
      ExamRegistrationTokenIssuedEvent   -> Issued
      ExamRegistrationTokenRevokedEvent  -> Revoked
      ExamRegistrationTokenRedeemedEvent -> Redeemed
    Initial: NotIssued
    Guards:
      NotIssued -> "Exam registration token not found."
      Revoked   -> "Exam registration token has been revoked."
      Redeemed  -> "Exam registration token has already been used."

### 6.4 Command Handlers

IssueExamRegistrationTokenCommandHandler:
  Optional: CourseExists(courseId) guard
  Action: unconditional append of ExamRegistrationTokenIssuedEvent
  Returns: TokenId

RedeemExamRegistrationTokenCommandHandler:
  1. BuildDecisionModelAsync(ExamTokenStatus(tokenId))
  2. Guard: not Issued -> fail
  3. AppendAsync(ExamRegistrationTokenRedeemedEvent, appendCondition)
  Wrapped in ExecuteDecisionAsync.

RevokeExamRegistrationTokenCommandHandler:
  1. BuildDecisionModelAsync(ExamTokenStatus(tokenId))
  2. Guard: NotIssued -> fail "token not found"
     Guard: Redeemed  -> fail "cannot revoke an already-redeemed token"
  3. AppendAsync(ExamRegistrationTokenRevokedEvent, appendCondition)

### 6.5 API Endpoints

Scalar tag: "Exam Registration (Opt-In Token Pattern)"

POST   /exams/{examId}/registration-tokens              Issue token (instructor) -> { tokenId }
POST   /exams/registration-tokens/{tokenId}/redeem      Redeem token (student)
DELETE /exams/registration-tokens/{tokenId}             Revoke token (instructor)
GET    /exams/{examId}/registration-tokens              List tokens (optional read model)

### 6.6 Tests

Unit tests (~10):
  ExamRegistrationTokenProjectionsTests:
    TokenWasIssued -- initial state, apply, query tag scoping
    TokenWasRedeemed -- initial state, apply, query tag scoping
    ExamTokenStatus -- NotIssued initial; Issued->Redeemed; Issued->Revoked;
                       Revoked blocks redeem; other-token events ignored

Integration tests (~7):
  ExamRegistrationTokenIntegrationTests:
    Issue token -> 201 with tokenId
    Redeem valid token -> 200 OK
    Redeem unknown token -> 400 not found
    Redeem already-used token -> 400 already used
    Revoke token -> 200 OK
    Redeem revoked token -> 400 revoked
    Revoke already-redeemed token -> 400 error

### 6.7 Implementation Order

1. Events (all three records)
2. Decision projections (TokenWasIssued, TokenWasRedeemed, ExamTokenStatus)
3. Unit tests
4. Issue command (unconditional append)
5. Redeem command (enum projection)
6. Revoke command
7. Integration tests
8. CHANGELOG

---

## 7. Open Questions

| # | Question | Default |
|---|---|---|
| 1 | Two bool projections or one ExamTokenStatus enum? | ExamTokenStatus enum |
| 2 | Student must be registered to redeem? | Yes -- StudentExists(studentId) as third projection |
| 3 | Store IssuedToStudentId on the token? | Yes -- prevents wrong student from redeeming |
| 4 | Persistent read model per exam? | Optional -- not needed for correctness |
| 5 | Exam seat capacity projection? | Out of scope for base |
| 6 | Token expiry? | Out of scope -- needs time-injection pattern |

---

## 8. Relationship to Other Examples

| Example | Similarity | Key Difference |
|---|---|---|
| Prevent Record Duplication | Single-use token, tag-scoped guard | Client-generated vs server-generated; one command vs two |
| Course Subscriptions | Enrollment-adjacent | Different domain, different event types, no shared projections |
| Dynamic Course Book Price | Time-aware projection | Token expiry would use same DateTimeOffset now pattern |

---

## 9. Summary

The "Opt-In Token" example maps to **Exam Registration Token**: instructor issues a
single-use access token per exam, student redeems it to register. Completely independent
of course enrollment.

Opossum is fully capable today. The two-step flow uses a single ExamTokenStatus enum
projection with BuildDecisionModelAsync<TState>, an unconditional issue append, and
ExecuteDecisionAsync for retry. The key teaching point -- DCB replaces a persistent
"valid tokens" read model -- is demonstrated cleanly: no IProjectionDefinition for
token state is needed for correctness.
