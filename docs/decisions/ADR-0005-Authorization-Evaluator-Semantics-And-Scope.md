# ADR-0005: Authorization Evaluator Semantics, Tenant Isolation and Scope

## Status

Accepted — **superseded in part by [ADR-0006](ADR-0006-Personnel-Tenancy-External-Identity-And-Root-GSA.md) and [ADR-0007](ADR-0007-Optimistic-Concurrency-And-Authorization-Cache.md)** (personnel pool / unrestricted GlobalSuperAdmin / "no caching" limitation). The evaluator semantics below remain authoritative unless a newer ADR says otherwise.

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
2. **Global super-admin:** an active, unexpired `GlobalSuperAdmin` role held through any active membership allows everything (`ALLOWED_GLOBAL_SUPER_ADMIN`). *(Root-company restriction: see ADR-0006.)*
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

### Admin Authority on grant paths (section 32)

Holding `Role.Permissions.Manage` or `Role.BulkAssign` is necessary but not sufficient. `UpdateRolePermissions`, `CopyRolePermissions` and `BulkAssign` also require `IAdminAuthority`, which reuses the evaluator's session:

- **Managing role.** A candidate M is a role the actor holds directly in the workspace company, is `Standard`, active and unexpired, and has the target role as a **strict** descendant. The target can never be M itself.
- **Effective permission.** For every entry, M's branch **alone** must effectively allow the permission. It follows the same Role Up, DENY boundaries (M's path and ancestors) and prerequisites as above. The actor's direct ALLOWs and delegations do not count. The actor's direct DENY still applies globally.
- **Scope superset.** The requested scope must be covered by M:
  - `None` is evaluated as an unscoped request;
  - each `Selected` pair is evaluated on its own;
  - `All` requires a `ScopeMode.All` ALLOW on M's branch, and any DENY on the branch blocks it.
- **No combining.** Each M is checked on its own. Two roles that each cover part of a request (the permission and the target, or different scope keys) do not add up to authority.
- **Super-admins.** A valid company or global super-admin is exempt (section 31).
- **Per path:**
  - `UpdateRolePermissions` checks every entry, DENY entries included: a manager cannot restrict what it does not hold.
  - `CopyRolePermissions` checks every copied rule, and requires authority over the target in both `APPEND` and `REPLACE` mode.
  - `BulkAssign` requires the assigned role to be a strict descendant of M.
- A failed check returns **403** and changes nothing.
- Because of Role Up, M's branch includes rules already held by its descendants. A manager may therefore re-grant, within its own subtree, anything that subtree already holds.

### Single authorization application

Administration is locked to one application, `AccessControlApplication.Code = "QC"`. `PermissionAuthorizationHandler` evaluates permissions against it. Every role handler and the resource tree only see QC roles and resources, so any other `ApplicationId` returns **404**. The role list has no application filter.

### Account status

- An account is usable only when `IsActive` is true and `IsDeleted` is false.
- `ApplicationSignInManager` refuses to issue tokens for an unusable account (**401** on `/login` and `/refresh`), and rejects the security stamp of an already-issued token.
- `WorkspaceContextMiddleware` resolves no workspace for an unusable account, so every business endpoint answers **403** even while an old token is still unexpired.
- At most one non-deleted account links to a given personnel record. This is enforced by a filtered unique index on `AspNetUsers.PersonnelId`.

### Organization lifecycles

All lifecycle rules take their time from `TimeProvider`, and a violation returns **409** with a stable `code`.

- **Personnel**: employment is never set directly (`PERSONNEL_STATUS_TRANSITION_INVALID`); it follows an effective position assignment. Deactivation, including `DELETE`, is a soft delete and requires no effective position. Reactivation returns to Employed when a position is effective, otherwise to Draft. Inactive personnel cannot be assigned (`PERSONNEL_INACTIVE`).
- **Positions**: a position with current or upcoming assignments cannot be deactivated (`POSITION_HAS_ACTIVE_ASSIGNMENTS`). Nobody can be assigned, or have an assignment reactivated, to an inactive position (`POSITION_INACTIVE`).
- **Hierarchies**: a company, position or role cannot be its own parent or form a cycle (`HIERARCHY_CYCLE`).

### Request contract

- Request fields that the handlers would ignore are not part of the contract; there is no role or role-assignment effective dating.
- FluentValidation rejects malformed requests with **400** `ValidationProblemDetails` (with an `errors` map) before a handler runs. It checks required codes and names with their column limits, a 10-digit national code, known enum values, `EffectiveTo > EffectiveFrom`, the copy mode (`APPEND` or `REPLACE`), a non-empty bulk-assign list, and pagination (`page >= 1`, `1 <= pageSize <= 100`).

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
