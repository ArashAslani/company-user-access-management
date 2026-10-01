# Company Access Management

[![Build and Test](https://github.com/ArashAslani/company-user-access-management/actions/workflows/build.yml/badge.svg)](https://github.com/ArashAslani/company-user-access-management/actions/workflows/build.yml)

A portfolio / reference implementation of a multi-company **Organization, Identity & Authorization** backend. It is built with **ASP.NET Core 10**, **EF Core 10 on SQLite** and **Clean Architecture** as a modular monolith.

The goal is a small codebase whose behaviour is fully pinned by tests. It is not a production-hardened product. See [Known limitations](#known-limitations).

## What it does

It keeps three concerns separate:

- **Identity** (ASP.NET Core Identity, bearer tokens) answers *who you are*.
- **Organization** (companies, positions, personnel, effective-dated assignments) answers *where you work*.
- **Authorization** (roles, permissions, scopes, delegation) answers *what you can do*.

**Position ≠ Role.** Holding a position never grants system access by itself.

### Authorization semantics

These rules are recorded in [ADR-0005](docs/decisions/ADR-0005-Authorization-Evaluator-Semantics-And-Scope.md) (superseded in part by [ADR-0006](docs/decisions/ADR-0006-Personnel-Tenancy-External-Identity-And-Root-GSA.md) / [ADR-0007](docs/decisions/ADR-0007-Optimistic-Concurrency-And-Authorization-Cache.md)) and each one has a named test.

| Rule | Behaviour |
|---|---|
| Role Up | A user holding role R is allowed what R **or any descendant** of R allows. Ancestor ALLOWs are not inherited downward. |
| Role Down / DENY boundary | A DENY on any role between the origin role and R, or on any ancestor of R, blocks that branch. A shared ancestor's DENY blocks every branch beneath it. A sibling's DENY does not block another branch. |
| Direct user DENY | Global. It wins over every role branch and delegation, including a DENY on a prerequisite. |
| Prerequisites | For example, `Products.Edit` requires `Products.Read`. Prerequisites are evaluated recursively, and a prerequisite denied on the candidate's own path blocks it. |
| Validity | Inactive or expired roles grant nothing, but their DENYs still apply. Super-admin roles follow the same validity rules. |
| Scopes (fail-closed) | `None` matches only unscoped requests, `All` matches any scope, and `Selected` matches only an exact `(type, key)` pair. |
| Delegation | Valid only while the delegator is an active member of the same company and is still allowed the permission, not counting delegations. |
| Super admins | `CompanySuperAdmin` applies within its company; `GlobalSuperAdmin` applies across companies **only when the role belongs to a root company** (`ParentCompanyId == null`). These kinds cannot be created, edited or escalated to through the API. |
| Admin authority | Changing a role's rules (update, copy) or assigning it (bulk assign) also requires **one** of the actor's roles to be an ancestor of the target role and to hold, in its own branch, every granted permission at an equal or wider scope. Super admins bypass the check. |
| Single application | Administration is locked to the `QC` application. Roles and resources of any other application answer 404. |

`IAccessEvaluator.EvaluateAsync` returns an `AccessDecision { Allowed, ReasonCode, Sources[] }`. `Sources` is a per-request **decision trace**: it records which rule and role produced the grant. It is **not** a persisted audit log. An `AuditLog` table exists in the schema, but nothing writes to it yet.

### Tenant isolation

Every business request carries `Authorization: Bearer <token>` and `X-Company-Id: <companyId>`.

- `WorkspaceContextMiddleware` accepts the header only for an active membership of that company.
- Handlers take the company from the workspace, never from the payload.

| Situation | Response |
|---|---|
| No or invalid token, or an inactive or deleted account at login | 401 |
| A malformed request (missing or overlong field, bad national code, unknown enum, reversed date range, pagination out of bounds) | 400, with an `errors` map |
| Missing permission, an `X-Company-Id` without an active membership, or an account deactivated after its token was issued | 403 |
| A body or query `companyId` that differs from the workspace | 403 |
| Another company's position, role, personnel, assignment or membership, referenced by id | 404 |
| A domain rule violation (overlap, cycle, duplicate code, sealed assignment, external identity conflict, …) | 409, with a `code` in the ProblemDetails |
| A concurrent writer changed the same personnel or company revision | 409 `CONCURRENCY_CONFLICT` |

### Tenant model

Companies are independent tenants. Personnel belongs to exactly one company (immutable `CompanyId`). There is no global unassigned personnel pool. Cross-tenant reads and writes by id return 404.

The same `NationalCode` can exist in different companies; within one company it is unique and editable.

Organization entities (`Company`, `Position`, `Personnel`, `PersonnelPosition`) can carry `ExternalSource` + `ExternalId` for ERP/HR synchronization. External identity, not the database Guid or NationalCode, is used for integration matching via `IExternalOrganizationResolver`. This repository is integration-ready; it is not the ERP/HR poller itself.

Authorization decisions use in-memory revision-keyed caching ([ADR-0007](docs/decisions/ADR-0007-Optimistic-Concurrency-And-Authorization-Cache.md)). Roles may have an optional expiry (ValidUntil), but they do not have ValidFrom, and UserRole assignments are not effective-dated.

Signature ingestion/versioning is implemented (magic bytes, decode, versions). Signature retrieval/history/workflow consumption is outside portfolio scope.

Personnel visibility is company ownership, not assignment-based pooling.

## Architecture

```mermaid
graph TD
    Web[Web: minimal API endpoints, auth pipeline] --> Infrastructure
    Web --> Application
    Infrastructure[Infrastructure: EF Core SQLite, Identity, AccessEvaluator] --> Application
    Application[Application: MediatR handlers, validation, tenancy helpers] --> Domain
    Domain[Domain: entities, domain rules]
```

`tests/ArchitectureTests` (NetArchTest) enforces these dependency rules:

- Domain references no Application, Infrastructure, Web, ASP.NET Core or EF Core.
- Application references no Infrastructure or Web.
- Infrastructure references no Web.

The request pipeline runs in this order:

1. `UseAuthentication`
2. `WorkspaceContextMiddleware` (resolves `X-Company-Id`)
3. `UseAuthorization`

On each endpoint, `RequirePermission("Resource.Action")` then calls the evaluator. The handler fails closed when the user, the workspace or the permission is missing.

```mermaid
erDiagram
    COMPANY ||--o{ USER_COMPANY : has
    COMPANY ||--o{ POSITION : has
    COMPANY ||--o{ ROLE : has
    POSITION ||--o{ POSITION : parent
    PERSONNEL ||--o{ PERSONNEL_POSITION : has
    PERSONNEL_POSITION }|--|| POSITION : assigns
    USER_COMPANY ||--o{ USER_ROLE : has
    ROLE ||--o{ USER_ROLE : assigns
    ROLE ||--o{ ROLE : parent
    USER_COMPANY ||--|| AUTH_PRINCIPAL : maps
    ROLE ||--|| AUTH_PRINCIPAL : maps
    AUTH_PRINCIPAL ||--o{ ACCESS_RULE : has
    ACCESS_RULE ||--o{ RULE_SCOPE : has
    PERMISSION ||--o{ ACCESS_RULE : secures
    PERMISSION ||--o{ PERMISSION_IMPLICATION : requires
```

The domain enforces the following rules:

- **Position and role hierarchies** reject cycles (409 `HIERARCHY_CYCLE`).
- **Personnel positions** are effective-dated: `IsCurrentlyEffective = Active ∧ From ≤ now ∧ (To = null ∨ To > now)`, derived at read time rather than stored.
- Overlapping assignments to the same position are rejected.
- At most one primary position per company may be effective at a time.
- Assignments whose effective window has ended are sealed.
- **Personnel** status follows assignments: employment is never set directly, deactivation (including `DELETE`) is a soft delete that requires no effective position, and inactive personnel cannot be assigned.
- **Positions** with current or upcoming assignments cannot be deactivated, and inactive positions accept no assignments.
- An account links to at most one personnel record, enforced by a filtered unique index.

## Running locally

```bash
git clone https://github.com/ArashAslani/company-user-access-management.git
cd company-user-access-management

dotnet restore
dotnet build
dotnet run --project src/Web      # http://localhost:5000, Scalar UI at /scalar
```

On startup in Development, the app applies EF Core migrations (never `EnsureCreated`) to `CompanyAccessManagement.db` and runs an idempotent seeder. The seeder creates:

- the `Administrator` identity role;
- the `administrator@localhost` / `Administrator1!` user;
- the `QC` application with its resources, permissions and prerequisites.

### Demo workspace (Development only)

`appsettings.Development.json` sets `Demo:Enabled=true`, which adds an idempotent demo workspace on top of the base seed. It never runs outside the Development environment, even if the flag is set elsewhere. It creates:

- `Demo Holding` (`d3e00000-0000-0000-0000-000000000001`) and, under it, `Demo Company` (`d3e00000-0000-0000-0000-000000000002`);
- a membership (`UserCompany` with its principal) for `administrator@localhost` in Demo Company;
- a root role `DEMO_ADMIN` holding every QC permission (`ScopeMode.All`), assigned to that membership.

With the flag off, the seed creates no companies or memberships and every business endpoint returns 403: there is no API for companies or memberships yet.

## API

| Area | Routes |
|---|---|
| Identity (ASP.NET Core Identity) | `POST /register`, `POST /login`, `POST /refresh`, `/manage/*`, … |
| Positions | `GET/POST /api/v1/organization/positions`, `GET/PUT/DELETE /api/v1/organization/positions/{id}`, `GET /api/v1/organization/positions/{id}/summary`, `GET /api/v1/organization/positions/tree?holdingId=` |
| Personnel | `GET/POST /api/v1/organization/personnel`, `GET/PUT/DELETE /api/v1/organization/personnel/{id}`, `POST /api/v1/organization/personnel/{id}/positions`, `PUT/DELETE /api/v1/organization/personnel/{personnelId}/positions/{assignmentId}`, `POST /api/v1/organization/personnel/{id}/signature` |
| Roles | `GET/POST /api/v1/access-control/roles`, `GET/PUT/DELETE /api/v1/access-control/roles/{id}`, `GET /api/v1/access-control/roles/tree?holdingId=`, `PUT /api/v1/access-control/roles/{id}/permissions`, `POST /api/v1/access-control/roles/{id}/permissions/copy-from`, `POST /api/v1/access-control/roles/bulk-assign` |
| Scopes | `GET /api/v1/access-control/scopes/resources/tree?applicationId=` |
| Docs | `GET /openapi/v1.json`, `GET /scalar` |

Permission names are canonical `Resource.Action` codes, e.g. `Organization.Position.Read` and `AccessManagement.Role.Permissions.Manage`. [`src/Web/Web-webapi.http`](src/Web/Web-webapi.http) contains ready-made requests.

```bash
# 1. Log in and keep the accessToken
curl -X POST http://localhost:5000/login -H "Content-Type: application/json" \
  -d '{"email":"administrator@localhost","password":"Administrator1!"}'

# 2. Call business endpoints in the demo company workspace
curl http://localhost:5000/api/v1/organization/positions \
  -H "Authorization: Bearer <accessToken>" -H "X-Company-Id: d3e00000-0000-0000-0000-000000000002"
curl http://localhost:5000/api/v1/access-control/roles \
  -H "Authorization: Bearer <accessToken>" -H "X-Company-Id: d3e00000-0000-0000-0000-000000000002"
```

## Testing

```bash
dotnet test CompanyAccessManagement.slnx
```

All tests run the real stack: migrations applied to a temporary file-backed SQLite database, with no EF InMemory provider. The API tests go through `WebApplicationFactory<Program>` with real Identity bearer tokens.

| Project | Tests | Covers |
|---|---|---|
| `tests/ArchitectureTests` | 3 | Layer dependency rules |
| `tests/Infrastructure.IntegrationTests` | 118 | Evaluator semantics (Role Up/Down, DENY boundaries, prerequisites, validity, delegation, root-company GlobalSuperAdmin, scopes), revision-keyed cache, two-context concurrency, external identity constraints and resolver, organization domain and lifecycle rules, forward-migration data backfills, empty-database migration and seed-twice idempotency |
| `tests/Web.ApiIntegrationTests` | 173 | 401/403/2xx outcomes, the permission handler and CORS, tenant isolation (403/404), national-code and external-identity APIs, signature decode validation, parallel concurrency invariants, end-to-end cache revoke, the QC application lock, admin authority on grant paths, account status, lifecycles, 400 contract validation, sensitive-data logging, the demo workspace, role/position/personnel workflows, and the OpenAPI surface |

CI ([build.yml](.github/workflows/build.yml)) runs restore, a Release build and the full test suite on every push and pull request to `main`.

## Known limitations

- There is no API for companies, memberships or delegations; they are created through the data layer.
- The access decision trace is not persisted, and the `AuditLog` table is unused.
- The following are intentionally out of scope ([ADR-0005](docs/decisions/ADR-0005-Authorization-Evaluator-Semantics-And-Scope.md), [ADR-0006](docs/decisions/ADR-0006-Personnel-Tenancy-External-Identity-And-Root-GSA.md)):
  - a correction/versioning workflow for sealed assignments;
  - Workshop entities;
  - generic attachments;
  - a frontend;
  - signature retrieval / history / workflow consumption;
  - Redis or any distributed cache;
  - an ERP/HR poller or ETL.
- Admin authority is enforced on grant paths only (role permission update, copy and bulk assign). Creating, editing and deleting roles is gated by the endpoint permission alone.
- Administration covers a single application (`QC`).
- Roles may have an optional expiry (ValidUntil), but they do not have ValidFrom, and UserRole assignments are not effective-dated. Only personnel positions are effective-dated.
- Concurrent role or rule edits in the same company can collide on `Company.AuthorizationRevision` (409 `CONCURRENCY_CONFLICT`); that is intentional ([ADR-0007](docs/decisions/ADR-0007-Optimistic-Concurrency-And-Authorization-Cache.md)).
- SQLite only.

## Architecture decisions

See [docs/decisions](docs/decisions/README.md) for the full index and the design documents. Newer Accepted ADRs take precedence over older ones.

- [ADR-001](docs/decisions/ADR-001-Use-EFCore-In-Application-Layer.md): EF Core in the Application layer
- [ADR-002](docs/decisions/ADR-002-Aspire-For-Orchestration-And-Testing.md): Aspire for local orchestration
- [ADR-003](docs/decisions/ADR-003-MediatR-Contracts-In-Domain.md): MediatR contracts in Domain
- [ADR-0004](docs/decisions/ADR-0004-RoleGroup-Removal-PersonnelPosition-Effective-Dating.md): RoleGroup removal and PersonnelPosition effective dating
- [ADR-0005](docs/decisions/ADR-0005-Authorization-Evaluator-Semantics-And-Scope.md): Evaluator semantics, tenant isolation and scope (superseded in part by ADR-0006 / ADR-0007)
- [ADR-0006](docs/decisions/ADR-0006-Personnel-Tenancy-External-Identity-And-Root-GSA.md): Personnel tenancy, external identity and root-company Global Super Admin
- [ADR-0007](docs/decisions/ADR-0007-Optimistic-Concurrency-And-Authorization-Cache.md): Optimistic concurrency and revision-keyed authorization cache
## Technology stack

- ASP.NET Core 10 minimal APIs, ASP.NET Core Identity (bearer tokens)
- EF Core 10 with SQLite and migrations
- MediatR, FluentValidation, AutoMapper, Ardalis.GuardClauses
- Scalar and OpenAPI
- NUnit, Shouldly and NetArchTest for testing
- GitHub Actions for CI

## Origins / Attribution

This solution was **bootstrapped from [Jason Taylor's Clean Architecture Solution Template](https://github.com/jasontaylordev/CleanArchitecture)** (v10.0) and then substantially adapted for the Company Access Management domain:

- Removed the template's sample features (Todo, WeatherForecast, Colour), SPA frontends, packaging, test-template CI and CodeQL.
- Renamed `CleanArchitecture.*` to `CompanyAccessManagement.*`.
- Removed the PostgreSQL and SQL Server options; the project is SQLite only.
- Implemented the Organization, Identity-membership and Authorization domains described above.

## License

MIT License. See [LICENSE](LICENSE).
