# ADR-0009: Effective Scopes Query

## Status

Accepted

## Date

2026-10-20

## Context

QC runs in its own pod and renders filtered lists (for example workstations). For a list it needs every scope key the current user can use for a permission, not a point check of one known key. `IAccessEvaluator.EvaluateAsync` answers only the point question.

Workshop and Workstation stay outside this service (design FINAL-1.1 §41). Scope keys are opaque strings. A projected read model fed by authorization events would have to reimplement DENY, Role Up/Down, prerequisite gates, delegation validity and super-admin rules, and would drift from the evaluator.

## Decision

### Contract

`GET /api/v1/access-control/effective-scopes?applicationCode=&permissionCode=&scopeType=`

The caller is the authenticated user in the current workspace. The endpoint does not accept a `userId`. Its permission is `AccessManagement.EffectiveScopes.Read` (catalogue and seed).

```json
{
  "scopeMode": "All | Selected",
  "scopeType": "Workstation",
  "scopeKeys": ["F", "V"],
  "revision": "<membershipRevision>.<companyAuthorizationRevision>.<policyRevision>"
}
```

`scopeKeys` is present only for `Selected`. An empty list means no key is allowed. `All` means every key. The response `ETag` is the quoted `revision`. `If-None-Match` yields 304 when it matches. A global super admin's token is prefixed with `gsa.{rootCompanyAuthorizationRevision}.` so losing that role changes the ETag.

### Computation and invariant

`EvaluateEffectiveScopesAsync` uses the same `EvaluationSession` as point evaluation: the role graph, allow candidates (direct, Role Up branches, valid delegations), path DENY and prerequisite gates. Both paths call `CandidateScope`, which is the set of keys one allow candidate still grants after branch-local DENYs on its own path.

For permission P and scope type T:

- `ScopeMode.All` contributes every key. `Selected` contributes keys of type T. `None`, or `Selected` of another type, contributes nothing.
- A path DENY of `All` drops that candidate. A path DENY of `Selected` on T removes only those keys from that candidate. Independent branches are not affected. A DENY on a shared ancestor is on every descendant path, so it removes the key from each of them.
- The caller's direct DENY (of P or a transitive prerequisite) removes those keys from the whole result, matching the point evaluator's global direct-DENY check.
- P's set is intersected with the effective set of each direct prerequisite, so Edit never returns a key Read does not grant.
- A delegation contributes the intersection of its rule and the delegator's effective set computed with `excludeDelegation: true`.
- Company or global super admin yields `All`.
- The union is `All` when any remaining contribution is unrestricted; otherwise `Selected` with distinct, ordinal-sorted keys.

Invariant: for every key `k`, `k` is in the result if and only if `EvaluateAsync` allows P at scope type T and scope key `k`. Integration tests assert this on a fixed universe for the scenarios above.

### Cache and size

The result uses the ADR-0007 revision-keyed `IMemoryCache`. The key is `(userId, companyId, appCode, permissionCode, scopeType, membershipRevision, companyAuthorizationRevision, policyRevision)`. Lifetime is the same `min(5 minutes, earliest validity boundary)` rule, and reads check `TimeProvider`. Global super admin is not cached. Evaluations with `excludeDelegation: true` (delegation-source checks) are not written to this cache.

If the finite key list is larger than `EffectiveScopes:MaxKeys` (default 5000), or the result is an unrestricted grant with exceptions (which this contract cannot express without claiming `All`), the call fails with 409 `EFFECTIVE_SCOPES_TOO_LARGE`. The list is not truncated.

## Rationale

Rejected for now: an event-projected read model of effective scopes. It would duplicate the evaluator, go stale relative to delegation expiry and role validity, and still need this invariant. QC can cache the live answer with `ETag` / `If-None-Match`. Revisit a projection only if the size ceiling is hit in production or `All`-with-exceptions becomes a real grant shape.

## Consequences

- QC list filters call one endpoint instead of one evaluation per row.
- DENY, Role Up/Down, prerequisite, delegation and super-admin rules are unchanged; they are shared with point evaluation through `CandidateScope`.
- A grant of `All` minus a selected DENY is an error, not a silent `All`.
- No Workshop or Workstation entity is introduced.
