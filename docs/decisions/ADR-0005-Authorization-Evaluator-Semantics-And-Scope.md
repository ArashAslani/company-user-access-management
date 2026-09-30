# ADR-0005: Authorization Evaluator Semantics, Tenant Isolation and Scope

## Status

Accepted

## Date

2026-09-30

## Context

The design document ([FINAL-1.1](organization-identity-authorization-design-defense-final-v1.1.md)) defines Role Up (section 28), Role Down (section 29), prerequisites, scopes and delegation (section 33). An audit of the earlier implementation found that it did not match the document:

- direct DENY was branch-local;
- the branch path ignored the assigned role's ancestors;
- inactive and expired roles still granted;
- a delegation stayed valid after the delegator lost the permission;
- `Role.PrincipalId` competed with `AuthPrincipal` as the role-to-principal mapping.

Handlers also trusted a `companyId` taken from the request body or query string instead of the validated workspace. Several API surfaces (Workshop, generic attachments, audit history) were stubs that returned fake data.

This ADR records the semantics the evaluator (`AccessEvaluator`) now implements. It also records how the Organization and AccessControl handlers are scoped to a tenant, and what is out of scope. Every rule below is covered by a test in `tests/Infrastructure.IntegrationTests/AuthorizationEngineTests.cs` or `tests/Web.ApiIntegrationTests`.

## Decision

### Evaluation order

For a request `(user, company, application, permission, scopeType?, scopeKey?)`:

1. **Unknown application or permission:** denied (`DENIED_APPLICATION`, `DENIED_PERMISSION_NOT_FOUND`).
2. **Global super-admin:** an active, unexpired `GlobalSuperAdmin` role held through any active membership allows everything (`ALLOWED_GLOBAL_SUPER_ADMIN`).
3. **Membership:** there must be an active `UserCompany` in the request company, otherwise `DENIED_MEMBERSHIP`.
4. **Company super-admin:** an active, unexpired `CompanySuperAdmin` role in that company allows everything (`ALLOWED_COMPANY_SUPER_ADMIN`).
5. **Rule evaluation:** everything else goes through the rule evaluation below.

### Rules

- **Direct DENY is global.** A scope-matched DENY on the user's own principal, whether on the permission or on any transitive prerequisite, denies the request whatever the role branches say.
- **ALLOW candidates** are the union of:
  - direct user ALLOWs;
  - role-branch ALLOWs;
  - valid delegations.
- **Role Up (section 28).** For a role R assigned to the user, an ALLOW held by R *or any descendant* of R is a candidate. An ALLOW on an ancestor of R is **not** inherited downward.
- **Role Down (section 29), branch-local DENY.** A branch candidate is blocked by a DENY on any role on the path from the origin role up to R, and on every ancestor of R. This means:
  - a DENY on a shared ancestor blocks all sibling branches beneath it;
  - a DENY on one sibling does not block a different sibling's ALLOW.
- **Validity.** Inactive or expired roles grant nothing, and every role on the grant path must be grantable. **Their DENY rules still apply**: a DENY on an inactive ancestor still blocks the branch.
- **Prerequisites** (`PermissionImplication`, e.g. `Edit` requires `Read`):
  - a prerequisite denied on the candidate's own path blocks that candidate (`DENIED_PREREQUISITE_GATE`);
  - each direct prerequisite must itself evaluate to allowed, recursively;
  - a prerequisite cycle is never satisfied.
- **Scopes are fail-closed:**
  - `None` matches only a request without a scope;
  - `All` matches any request;
  - `Selected` matches only an exact `(ScopeType, ScopeKey)` pair.
- **Delegation.** A delegated ALLOW is valid only while the delegator is an active member of the request company **and** is allowed the permission without counting any delegation. So no delegation chains, and the grant is lost as soon as the source loses it.
- **Single role authority.** A role's rules live only on its `AuthPrincipal` (`Type = Role`). `Role.PrincipalId` was removed by a forward migration that backfills any missing principals.

### Tenant isolation

- `WorkspaceContextMiddleware` accepts `X-Company-Id` only after validating an active membership, and exposes it as `ICurrentWorkspace.CompanyId`. No handler trusts a company id from the payload.
- **Naming another company** explicitly (a `companyId` in the body or query that differs from the workspace) returns **403**.
- **Reaching another company's entity by id** (a position, role, parent, assignment or membership) returns **404**.
- **Personnel** have no `CompanyId` (design document section 13.2). A person is visible in a workspace when they hold an active assignment to one of that company's positions, or hold no active assignment at all (the registration pool). Assigning someone to a position requires the position to belong to the workspace.
- **Super-admin kinds** cannot be created, edited or escalated to through the API (**403**). `Role.Kind` is immutable after creation.

### HTTP authorization

- `RequirePermission("Resource.Action")` carries the permission on the requirement itself.
- `PermissionAuthorizationHandler` fails closed when the user, the workspace company or the permission is missing.
- The outcomes are:
  - no token: 401;
  - no permission: 403;
  - wrong `X-Company-Id`: 403.

### Non-goals

These are deliberately not implemented, and their stub surfaces were removed:

- a correction/versioning workflow for sealed personnel assignments;
- a persistent decision audit. `AccessDecision.Sources` is a per-request **decision trace**. The `AuditLog` table exists, but nothing writes to it and no endpoint reads it;
- Workshop entities and workshop-based routing. `Workshop` is only a free-form scope type;
- generic attachments. Personnel signatures remain;
- a delegation write API. Delegated rules are evaluated, but can only be created through the data layer.

## Rationale

- **Global direct DENY** follows the design principle that an explicit, user-specific revoke must win. Letting a role branch override it would make revocation unreliable.
- **Branch-local role DENY** keeps unrelated role assignments independent. This is the behavior sections 28 and 29 describe.
- **Recursive evaluation, rather than flattening rules into a single set**, makes each candidate's path explicit. That is what lets the DENY boundaries and prerequisite gates be evaluated per branch.
- **Tenant isolation** separates a 403 for an explicit cross-company request from a 404 for an id that is simply not visible. This avoids confirming that another tenant's entity exists.

## Consequences

**Easier:**
- Each rule has a named test, so a regression shows up as a specific failing scenario.
- Handlers share one tenancy helper (`CurrentWorkspaceExtensions`), which makes it straightforward to add new endpoints safely.

**Harder:**
- The evaluator loads the company's role graph and relevant rules per request. There is no cache and no precomputed effective-permission table.
- Personnel visibility derived from positions means that someone in the unassigned registration pool is visible to every company.
