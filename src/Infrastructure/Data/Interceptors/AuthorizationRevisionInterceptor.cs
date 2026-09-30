using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CompanyAccessManagement.Infrastructure.Data.Interceptors;

/// <summary>
/// Bumps the revisions that key cached authorization decisions inside the same <c>SaveChanges</c> as the mutation,
/// so they commit (or roll back) atomically with it (ADR-0007):
/// <list type="bullet">
/// <item>Membership status, user roles and rules on a membership principal bump <see cref="UserCompany.AuthorizationRevision"/>.</item>
/// <item>Roles, role principals and their rules, delegated rules, removed memberships and any change to a delegator's
/// membership bump <see cref="Company.AuthorizationRevision"/>: those affect decisions keyed by other users.</item>
/// <item>Resources, permissions, implications and applications bump <see cref="Domain.AccessControl.Application.PolicyRevision"/>.</item>
/// </list>
/// </summary>
public sealed class AuthorizationRevisionInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
            BumpAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            await BumpAsync(eventData.Context, cancellationToken);

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static async Task BumpAsync(DbContext context, CancellationToken cancellationToken)
    {
        var changed = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        if (changed.Count == 0)
            return;

        var memberships = new HashSet<Guid>();
        var companies = new HashSet<Guid>();
        var applications = new HashSet<Guid>();
        var principals = new HashSet<Guid>();
        var delegatedPrincipals = new HashSet<Guid>();
        var ruleIds = new HashSet<Guid>();
        var resourceIds = new HashSet<Guid>();
        var permissionIds = new HashSet<Guid>();

        foreach (var entry in changed)
        {
            switch (entry.Entity)
            {
                case UserCompany uc when entry.State != EntityState.Added:
                    memberships.Add(uc.Id);
                    if (entry.State == EntityState.Deleted)
                        companies.Add(uc.CompanyId);
                    break;
                case UserRole ur:
                    memberships.Add(ur.UserCompanyId);
                    break;
                case AccessRule rule:
                    principals.Add(rule.PrincipalId);
                    if (rule.Origin == AccessRuleOrigin.Delegated)
                        delegatedPrincipals.Add(rule.PrincipalId);
                    break;
                case RuleScope scope:
                    ruleIds.Add(scope.AccessRuleId);
                    break;
                case AuthPrincipal principal:
                    principals.Add(principal.Id);
                    break;
                case Role role:
                    companies.Add(role.CompanyId);
                    break;
                case Resource resource:
                    applications.Add(resource.ApplicationId);
                    break;
                case Permission permission:
                    resourceIds.Add(permission.ResourceId);
                    break;
                case PermissionImplication implication:
                    permissionIds.Add(implication.PermissionId);
                    break;
                case Domain.AccessControl.Application application when entry.State != EntityState.Added:
                    applications.Add(application.Id);
                    break;
            }
        }

        foreach (var rule in await LoadAsync<AccessRule>(context, ruleIds, r => r.Id, cancellationToken))
        {
            principals.Add(rule.PrincipalId);
            if (rule.Origin == AccessRuleOrigin.Delegated)
                delegatedPrincipals.Add(rule.PrincipalId);
        }

        foreach (var principal in await LoadAsync<AuthPrincipal>(context, principals, p => p.Id, cancellationToken))
        {
            if (principal.Type == PrincipalType.Role || delegatedPrincipals.Contains(principal.Id))
                companies.Add(principal.CompanyId);
            if (principal.Type == PrincipalType.UserCompany)
                memberships.Add(principal.ReferenceId);
        }

        foreach (var permission in await LoadAsync<Permission>(context, permissionIds, p => p.Id, cancellationToken))
            resourceIds.Add(permission.ResourceId);

        foreach (var resource in await LoadAsync<Resource>(context, resourceIds, r => r.Id, cancellationToken))
            applications.Add(resource.ApplicationId);

        foreach (var membershipId in memberships)
        {
            var membership = await FindTrackedAsync<UserCompany>(context, membershipId, cancellationToken);
            if (membership is null)
                continue;

            membership.IncrementRevision();
            if (await IsDelegatorAsync(context, membership, cancellationToken))
                companies.Add(membership.CompanyId);
        }

        foreach (var companyId in companies)
            (await FindTrackedAsync<Company>(context, companyId, cancellationToken))?.TouchAuthorization();

        foreach (var applicationId in applications)
            (await FindTrackedAsync<Domain.AccessControl.Application>(context, applicationId, cancellationToken))?.IncrementPolicyRevision();
    }

    /// <summary>A delegator's roles, rules and status decide the delegatee's delegated permissions.</summary>
    private static Task<bool> IsDelegatorAsync(DbContext context, UserCompany membership, CancellationToken cancellationToken)
        => (from rule in context.Set<AccessRule>()
            join principal in context.Set<AuthPrincipal>() on rule.PrincipalId equals principal.Id
            where rule.Origin == AccessRuleOrigin.Delegated
                && rule.DelegatedFromUserId == membership.UserId
                && principal.CompanyId == membership.CompanyId
            select rule.Id).AnyAsync(cancellationToken);

    /// <summary>Tracked entities (including deleted ones) first, then the database for the rest.</summary>
    private static async Task<List<T>> LoadAsync<T>(DbContext context, HashSet<Guid> ids, Func<T, Guid> key, CancellationToken cancellationToken)
        where T : class
    {
        if (ids.Count == 0)
            return [];

        var tracked = context.ChangeTracker.Entries<T>().Select(e => e.Entity).Where(e => ids.Contains(key(e))).ToList();
        var missing = ids.Except(tracked.Select(key)).ToList();
        if (missing.Count == 0)
            return tracked;

        var loaded = await context.Set<T>().AsNoTracking()
            .Where(e => missing.Contains(EF.Property<Guid>(e, "Id")))
            .ToListAsync(cancellationToken);
        return [.. tracked, .. loaded];
    }

    private static async Task<T?> FindTrackedAsync<T>(DbContext context, Guid id, CancellationToken cancellationToken)
        where T : class
    {
        var entry = context.ChangeTracker.Entries<T>().FirstOrDefault(e => Equals(e.Property("Id").CurrentValue, id));
        if (entry is not null)
            return entry.State == EntityState.Deleted ? null : entry.Entity;

        return await context.Set<T>().FindAsync([id], cancellationToken);
    }
}
