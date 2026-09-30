# Architectural Decision Records

This directory contains the Architecture Decision Records (ADRs) and design documents for Company Access Management.

An ADR captures a significant architectural decision: the context that led to it, the decision itself, the rationale behind it, and its consequences. Use [ADR-000-template.md](ADR-000-template.md) when adding a new record.

| ADR | Title | Date | Status |
|---|---|---|---|
| [ADR-001](ADR-001-Use-EFCore-In-Application-Layer.md) | Use EF Core in the Application Layer | 2024-02-29 | Accepted (inherited from the template) |
| [ADR-002](ADR-002-Aspire-For-Orchestration-And-Testing.md) | Aspire for Orchestration and Testing | 2026-03-12 | Accepted for local orchestration (`src/AppHost`); tests use `WebApplicationFactory` with file-backed SQLite instead of Aspire test hosts |
| [ADR-003](ADR-003-MediatR-Contracts-In-Domain.md) | MediatR.Contracts Reference in Domain | 2026-03-16 | Accepted (inherited from the template) |
| [ADR-0004](ADR-0004-RoleGroup-Removal-PersonnelPosition-Effective-Dating.md) | RoleGroup removal and PersonnelPosition effective dating | — | Accepted |
| [ADR-0005](ADR-0005-Authorization-Evaluator-Semantics-And-Scope.md) | Authorization evaluator semantics, tenant isolation and scope | 2026-09-30 | Accepted |

## Design documents

- [Organization / Identity / Authorization design — FINAL-1.1](organization-identity-authorization-design-defense-final-v1.1.md) (the baseline the ADRs refer to)
- [Design defense v3](organization-identity-authorization-design-defense-v3.md) (earlier revision)
- [QC Access Management API specification v1](API-Specification-QC-Access-Management-v1.md) (the original specification; the implemented routes are listed in the repository README)
