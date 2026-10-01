# ADR-0006: Personnel Tenancy, External Identity and Root-Company Global Super Admin

## Status

Accepted

## Date

2026-10-01

## Context

Earlier documents ([ADR-0005](ADR-0005-Authorization-Evaluator-Semantics-And-Scope.md), [FINAL-1.1](organization-identity-authorization-design-defense-final-v1.1.md)) treated personnel as a shared pool: a person was visible in a workspace when they held an assignment in that company, or when they had no assignment at all. National codes were globally unique. `GlobalSuperAdmin` was granted from any company that held such a role. There was no first-class way to match organization rows to an ERP/HR system of record.

Those choices conflicted with the product model the portfolio now locks in: companies are independent tenants, a person belongs to exactly one company, and ERP/HR sync must be safe and idempotent.

## Decision

### Tenancy

- Every `Personnel` row has an immutable `CompanyId`. There is no global unassigned personnel pool.
- List, detail, update, delete, signature and assignment handlers load personnel with `VisibleIn(companyId)` (`p.CompanyId == workspace`).
- Cross-company access by id returns **404**, not 403.
- Positions may only be assigned when they belong to the same company (`PERSONNEL_COMPANY_MISMATCH`).

### National code

- `NationalCode` is editable via `ChangeNationalCode`.
- Uniqueness is **per company** (`IX_Personnel_CompanyId_NationalCode`). The same code may exist in different companies.

### Migration of existing rows

Forward migration `PersonnelCompanyOwnership`:

1. rows with exactly one distinct assigned company receive that company;
2. unassigned rows receive the sole root company when exactly one root exists;
3. otherwise the migration fails with the named check constraint `personnel_company_unresolved_assign_manually`.

### External organization identity

- Optional `ExternalSource` / `ExternalId` on `Company`, `Position`, `Personnel` and `PersonnelPosition`.
- Both null or both non-empty; source is `Trim().ToUpperInvariant()`; id is stored verbatim.
- Filtered unique indexes and a CHECK constraint; handlers pre-check duplicates (`EXTERNAL_IDENTITY_DUPLICATE`); SQLite unique races map to the same 409.
- `IExternalOrganizationResolver` resolves by external identity. Sync clients resolve, then create or update through the existing MediatR commands. There are no upsert endpoints.

### Global Super Admin

- A `GlobalSuperAdmin` role grants cross-company access **only** when its owning company is a root (`ParentCompanyId == null`).
- Inactive or expired GlobalSuperAdmin roles grant nothing (unchanged validity rules).

### Signature scope

- Ingestion and versioning are in scope (magic bytes, decode via ImageSharp, versioned rows).
- Retrieval, history and workflow consumption are out of scope.

### Role expiry wording

Roles may have an optional expiry (`ValidUntil`), but they do not have `ValidFrom`, and `UserRole` assignments are not effective-dated.

## Consequences

- ADR-0005's personnel-pool and unrestricted GlobalSuperAdmin wording are superseded by this ADR. The evaluator semantics (Role Up/Down, DENY, prerequisites, scopes, delegation, Admin Authority) remain those of ADR-0005.
- Existing tests that assumed the pool were rewritten to expect 404 / company mismatch.
- ERP/HR integration is domain-ready; this repository is not the poller or ETL.

## Supersedes (in part)

- ADR-0005 (personnel visibility / pool; GlobalSuperAdmin company unrestricted).
- Matching passages in the design documents that describe an unassigned personnel pool.
