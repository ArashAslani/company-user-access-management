# ADR-0007: Optimistic Concurrency and Revision-Keyed Authorization Cache

## Status

Accepted

## Date

2026-10-01

## Context

Primary-position and hierarchy mutations must not produce two effective primaries or a hierarchy cycle when two writers race. Process-local locks (`static`, `SemaphoreSlim`) are not acceptable in a portfolio that claims real persistence protection.

Authorization decisions are expensive (role graph, rules, prerequisites, delegation). Caching them must not serve a stale ALLOW after a role, membership or policy change, and must not outlive a role or rule validity boundary.

## Decision

### Optimistic concurrency (organization)

- `Personnel.ConcurrencyToken` (`Guid`, EF concurrency token) is rotated by a private `Touch()` on every aggregate mutation (details, national code, status, assignments, signature).
- `Company.OrganizationRevision` and `Company.AuthorizationRevision` (`long`, starting at 1, EF concurrency tokens) are bumped by `TouchOrganization()` / `TouchAuthorization()`.
- Position and role parent changes load the tracked company and bump the matching revision **before** the cycle check and save. A concurrent parent change therefore fails at `UPDATE … WHERE Revision = @old`.
- `DbUpdateConcurrencyException` maps to `ConcurrencyConflictException` → HTTP 409 with code `CONCURRENCY_CONFLICT`.
- Proof is two independent `ApplicationDbContext`s on one SQLite file. Parallel API tests only assert the invariant (statuses in {200/201, 409}); SQLite may serialize writers.

**Deviation from an early draft that used `Guid` revisions:** company revisions are `long` counters, consistent with the existing `UserCompany.AuthorizationRevision` and `Application.PolicyRevision`.

### Revision-keyed cache

- `IMemoryCache` only (no Redis / distributed cache). JWT permission claims are not used.
- GlobalSuperAdmin is evaluated first and is **never** cached. Non-members are denied and not cached.
- Cache key: `(userId, companyId, appCode, permissionCode, scopeType, scopeKey, membershipRevision, companyAuthorizationRevision, policyRevision)`.
- Entry lifetime: `min(now + 5 minutes, earliest future ValidUntil/ValidFrom among roles and rules that can affect the request)`. Absolute expiration on the entry is a backstop; every read also checks against `TimeProvider`.
- Admin Authority stays uncached.

### Revision responsibilities (`AuthorizationRevisionInterceptor`)

Committed atomically with the mutation:

| Revision | Bumped when |
|---|---|
| `UserCompany.AuthorizationRevision` | membership status; user role add/remove; access rules / scopes on a UserCompany principal |
| `Company.AuthorizationRevision` | role create/update/delete/status/hierarchy; role-principal rules (including copy); delegated rules; changes to a membership that is a delegator; deleted memberships |
| `Application.PolicyRevision` | resource, permission, implication or application catalogue changes |

Role permission update/copy no longer bump `PolicyRevision`; the interceptor bumps `Company.AuthorizationRevision` instead.

### Side effect

Because role and rule mutations also update the company row, two concurrent role edits in the same company can return 409 `CONCURRENCY_CONFLICT`. That is intentional and acceptable.

## Consequences

- ADR-0005's "no caching" limitation is superseded.
- Tests cover interceptor invalidation, temporal expiry (`FakeTimeProvider`), company/scope isolation, and two-context concurrency races.

## Supersedes (in part)

- ADR-0005 (known limitation: evaluator has no caching).
