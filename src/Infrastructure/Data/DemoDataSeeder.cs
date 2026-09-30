using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Organization;
using CompanyAccessManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CompanyAccessManagement.Infrastructure.Data;

/// <summary>
/// Development-only demo workspace: a holding, one company under it, the seeded administrator as a member,
/// and a root <c>DEMO_ADMIN</c> role holding every QC permission. Idempotent; ids are fixed so the
/// README walkthrough can use a known <c>X-Company-Id</c>.
/// </summary>
public class DemoDataSeeder
{
    public const string EnabledKey = "Demo:Enabled";
    public const string AdministratorEmail = "administrator@localhost";
    public const string RoleCode = "DEMO_ADMIN";

    public static readonly Guid HoldingId = new("d3e00000-0000-0000-0000-000000000001");
    public static readonly Guid CompanyId = new("d3e00000-0000-0000-0000-000000000002");

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<DemoDataSeeder> _logger;

    public DemoDataSeeder(ApplicationDbContext context, UserManager<ApplicationUser> userManager, ILogger<DemoDataSeeder> logger)
    {
        _context = context;
        _userManager = userManager;
        _logger = logger;
    }

    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration)
        => environment.IsDevelopment() && configuration.GetValue<bool>(EnabledKey);

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var administrator = await _userManager.FindByEmailAsync(AdministratorEmail)
            ?? throw new InvalidOperationException("The administrator account must be seeded before the demo workspace.");

        var qcApp = await _context.Applications
            .Include(a => a.Resources)
                .ThenInclude(r => r.Permissions)
            .SingleAsync(a => a.Code == AccessControlApplication.Code, cancellationToken);

        await EnsureCompanyAsync(HoldingId, "DEMO_HOLDING", "Demo Holding", parentCompanyId: null, cancellationToken);
        await EnsureCompanyAsync(CompanyId, "DEMO_COMPANY", "Demo Company", HoldingId, cancellationToken);

        var membership = await _context.UserCompanies
            .FirstOrDefaultAsync(uc => uc.UserId == administrator.Id && uc.CompanyId == CompanyId, cancellationToken);
        if (membership is null)
        {
            membership = new UserCompany(administrator.Id, CompanyId, Guid.Empty);
            var principal = AuthPrincipal.ForUserCompany(membership.Id, CompanyId, qcApp.Id);
            _context.UserCompanies.Add(membership);
            _context.Entry(membership).Property(uc => uc.PrincipalId).CurrentValue = principal.Id;
            _context.AuthPrincipals.Add(principal);
        }

        var role = await _context.BusinessRoles
            .FirstOrDefaultAsync(r => r.CompanyId == CompanyId && r.Code == RoleCode, cancellationToken);
        Guid rolePrincipalId;
        if (role is null)
        {
            role = new Role(CompanyId, qcApp.Id, RoleCode, "Demo Administrator");
            var rolePrincipal = AuthPrincipal.ForRole(role.Id, CompanyId, qcApp.Id);
            _context.BusinessRoles.Add(role);
            _context.AuthPrincipals.Add(rolePrincipal);
            rolePrincipalId = rolePrincipal.Id;
        }
        else
        {
            rolePrincipalId = await _context.AuthPrincipals
                .Where(p => p.Type == PrincipalType.Role && p.ReferenceId == role.Id)
                .Select(p => p.Id)
                .SingleAsync(cancellationToken);
        }

        var grantedPermissionIds = await _context.AccessRules
            .Where(r => r.PrincipalId == rolePrincipalId && r.Effect == AccessEffect.Allow)
            .Select(r => r.PermissionId)
            .ToListAsync(cancellationToken);
        foreach (var permission in qcApp.Resources.SelectMany(r => r.Permissions).Where(p => !grantedPermissionIds.Contains(p.Id)))
            _context.AccessRules.Add(new AccessRule(rolePrincipalId, permission.Id, AccessEffect.Allow, AccessRuleOrigin.Manual, ScopeMode.All));

        if (!await _context.UserRoles.AnyAsync(ur => ur.UserCompanyId == membership.Id && ur.RoleId == role.Id, cancellationToken))
            _context.UserRoles.Add(new UserRole(membership.Id, role.Id));

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Demo workspace is ready (company {CompanyId}).", CompanyId);
    }

    private async Task EnsureCompanyAsync(Guid id, string code, string name, Guid? parentCompanyId, CancellationToken cancellationToken)
    {
        if (await _context.Companies.AnyAsync(c => c.Id == id, cancellationToken))
            return;

        var company = new Company(code, name, parentCompanyId: parentCompanyId);
        _context.Entry(company).Property(c => c.Id).CurrentValue = id;
        _context.Companies.Add(company);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
