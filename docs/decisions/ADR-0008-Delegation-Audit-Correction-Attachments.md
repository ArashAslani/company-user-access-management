# ADR-0008: Delegation Write API, Persistent Audit, Assignment Correction and Attachments

## Status

Accepted

## Date

2026-10-02

## Context

ADR-0005 listed four capabilities as Non-goals: a delegation write API, a persistent decision audit, sealed-assignment correction/versioning, and generic attachments. The design documents ([FINAL-1.1](organization-identity-authorization-design-defense-final-v1.1.md) §§33, 42–45; [API specification](API-Specification-QC-Access-Management-v1.md) §0 / §1.2 / §3.2 / §4; ADR-0004 §2.7) already describe the intended behaviour. Completing them is now required for the portfolio surface without reopening the evaluator DENY / Role Up / prerequisite rules or introducing Workshop entities.

## Decision

### 1. Delegation write API (design §33)

- `POST /api/v1/access-control/delegations` (`AccessManagement.Delegation.Create`) and `DELETE /api/v1/access-control/delegations/{id}` (`AccessManagement.Delegation.Revoke`) create or deactivate an `AccessRule` with `Origin = Delegated`.
- Authority is assessed by `IDelegationAuthority` / `DelegationAuthority`:
  - ownership is checked through the same evaluation path as `IsDelegationSourceValidAsync` (`IAccessEvaluator.EvaluateAsync(..., excludeDelegation: true)`);
  - if the permission is held only through a delegated rule, the request is rejected with 409 `REDELEGATION_FORBIDDEN`;
  - the requested scope must be a subset of the delegator's non-delegated grant (`All` covers any request; `Selected` requires every `(ScopeType, ScopeKey)` to be present; otherwise 400);
  - `ValidUntil` must not exceed the earliest source validity boundary; a source with null `ValidUntil` imposes no cap (otherwise 409 `DELEGATION_VALID_UNTIL_EXCEEDS_SOURCE`).
- `excludeDelegation: true` results are never written to the revision-keyed decision cache, so they cannot poison normal evaluations.
- Success writes `DELEGATION_CREATED` / `DELEGATION_REVOKED` audit events. Only the original delegator may revoke.

### 2. Correction of sealed PersonnelPosition (ADR-0004 §2.7)

- `POST /api/v1/organization/personnel/{id}/positions/{assignmentId}/corrections` (`Organization.PersonnelPosition.Correct`) is allowed only when `assignment.IsSealed(now)`.
- The sealed row is left untouched. A new `PersonnelPosition` is inserted with a new Id and `CorrectsAssignmentId` pointing at the sealed row.
- Overlap and primary rules reuse the existing domain methods on `Personnel` (superseded rows are ignored so the sealed window does not block its own correction). A second correction of the same sealed row is 409 `ASSIGNMENT_ALREADY_CORRECTED`.
- A non-empty `Reason` is required and recorded in the audit `Metadata`. The audit event is `PERSONNEL_POSITION_CORRECTED` with BeforeData / AfterData carrying the old and new windows.

### 3. Persistent audit (design §§42–45)

- `IAuditWriter` / `IAuditOperation` live in Application; the EF-backed writer lives in Infrastructure. ArchitectureTests keep Application free of EF Core.
- Every listed write handler emits the matching `AuditEventTypes` constant. Multiple mutations in one MediatR request share one scoped `OperationId`.
- Signature and attachment audits store metadata only (ids, hashes, file name, mime, size) — never binary content.
- `AuditLog.OccurredAt` (`DateTime`, UTC) is the filterable / orderable timestamp (SQLite cannot order `DateTimeOffset` without a converter). Existing Created / LastModified audit columns remain.
- Read endpoint: `GET /api/v1/audit/access-history` (`AccessManagement.AuditLog.Read`), filterable by `actorUserId`, `companyId`, date range, `entityType`, `eventType` and `operationId`. No export or print. `PERMISSION_DENIED` and `ROLE_GROUP_MEMBERSHIP_CHANGED` are intentionally not written.
- `ROLE_REMOVED` is emitted by `DELETE /api/v1/access-control/roles/{id}/users/{userCompanyId}` (`AccessManagement.Role.Unassign`), the counterpart of bulk-assign.

### 4. Attachments for Position and Role (API §0)

- Domain entity `Attachment` (`OwnerType` = Position | Role) with FileName, MimeType, SizeBytes, ContentHash, Content, UploadedAt.
- Allowed types: PDF / XLSX / DOCX, max 8 MB. Magic bytes follow the signature pattern: PDF must start with `%PDF`; XLSX/DOCX (OOXML) must start with ZIP `PK`.
- Upload / download / delete are nested under the owning entity (`/api/v1/organization/positions/{id}/attachments…`, `/api/v1/access-control/roles/{id}/attachments…`), not a detached `/api/v1/attachments` surface. Upload and delete write `ATTACHMENT_UPLOADED` / `ATTACHMENT_DELETED`.

### Explicitly still out of scope

- Workshop as a real entity or workshop routing (`ScopeType = "Workshop"` remains a free string).
- Changes to DENY / role-branch / prerequisite logic in `AccessEvaluator`.
- Audit export / print, signature retrieval / history, a frontend, Redis, an ERP/HR poller.

## Consequences

- ADR-0005 Non-goals for delegation write, persistent audit, sealed-assignment correction and generic attachments are superseded by this ADR.
- A forward migration adds `Attachments`, `PersonnelPositions.CorrectsAssignmentId` and `AuditLogs.OccurredAt` (with a Created → OccurredAt backfill). Existing migrations are not rewritten.
- Portfolio API surface grows by the four feature areas above; OpenAPI continues to reject the removed stub `/api/v1/attachments` and `/api/v1/access-control/audit` paths.

## Supersedes (in part)

- ADR-0005 (Non-goals: delegation write API, persistent audit, sealed-assignment correction, generic attachments).
