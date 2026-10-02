using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Constants;
using CompanyAccessManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CompanyAccessManagement.Infrastructure.Data;

public static class InitialiserExtensions
{
    public static async Task InitialiseDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();

        await initialiser.InitialiseAsync();
        await initialiser.SeedAsync();

        if (DemoDataSeeder.IsEnabled(app.Environment, app.Configuration))
            await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();
    }
}

public class ApplicationDbContextInitialiser
{
    private readonly ILogger<ApplicationDbContextInitialiser> _logger;
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;

    public ApplicationDbContextInitialiser(
        ILogger<ApplicationDbContextInitialiser> logger,
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager)
    {
        _logger = logger;
        _context = context;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task InitialiseAsync()
    {
        try
        {
            await _context.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while initialising the database.");
            throw;
        }
    }

    public async Task SeedAsync()
    {
        try
        {
            await TrySeedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }

    public async Task TrySeedAsync()
    {
        await SeedIdentityAsync();
        await SeedQcCatalogueAsync();
    }

    private async Task SeedIdentityAsync()
    {
        var administratorRole = new IdentityRole<Guid>(Roles.Administrator);

        if (_roleManager.Roles.All(r => r.Name != administratorRole.Name))
            await _roleManager.CreateAsync(administratorRole);

        var administrator = new ApplicationUser
        {
            UserName = "administrator@localhost",
            Email = "administrator@localhost",
            IsActive = true
        };

        if (_userManager.Users.All(u => u.UserName != administrator.UserName))
        {
            await _userManager.CreateAsync(administrator, "Administrator1!");
            if (!string.IsNullOrWhiteSpace(administratorRole.Name))
                await _userManager.AddToRolesAsync(administrator, [administratorRole.Name]);
        }
    }

    /// <summary>
    /// Find-or-create the QC application catalogue (resources, permissions, implications). Safe to call repeatedly.
    /// </summary>
    private async Task SeedQcCatalogueAsync()
    {
        var qcApp = await _context.Applications
            .Include(a => a.Resources)
                .ThenInclude(r => r.Permissions)
                    .ThenInclude(p => p.Implications)
            .FirstOrDefaultAsync(a => a.Code == AccessControlApplication.Code);

        if (qcApp is null)
        {
            qcApp = new Domain.AccessControl.Application(AccessControlApplication.Code, "Quality Control", "Quality Control Application");
            _context.Applications.Add(qcApp);
        }
        else
        {
            qcApp.UpdateDetails("Quality Control", "Quality Control Application", isActive: true);
        }

        var products = EnsureResource(qcApp, "Products", "Products", "Product management");
        EnsurePermission(products, "Read", "Read products");
        EnsurePermission(products, "Edit", "Edit products");
        EnsurePermission(products, "Delete", "Delete products");

        var laboratory = EnsureResource(qcApp, "Laboratory", "Laboratory", "Laboratory management");
        EnsurePermission(laboratory, "Read", "Read laboratory data");
        EnsurePermission(laboratory, "Approve", "Approve laboratory results");

        var ncr = EnsureResource(qcApp, "NCR", "Non-Conformance Reports", "NCR management");
        EnsurePermission(ncr, "Read", "Read NCRs");
        EnsurePermission(ncr, "Approve", "Approve NCRs");

        var organization = EnsureResource(qcApp, "Organization", "Organization", "Organization management");
        EnsurePermission(organization, "Personnel.Read", "Read personnel");
        EnsurePermission(organization, "Personnel.Create", "Create personnel");
        EnsurePermission(organization, "Personnel.Edit", "Edit personnel");
        EnsurePermission(organization, "Personnel.Delete", "Delete personnel");
        EnsurePermission(organization, "PersonnelPosition.Create", "Create personnel position assignment");
        EnsurePermission(organization, "PersonnelPosition.Edit", "Edit personnel position assignment");
        EnsurePermission(organization, "PersonnelPosition.Delete", "Delete personnel position assignment");
        EnsurePermission(organization, "PersonnelPosition.Correct", "Correct historical personnel position assignment");
        EnsurePermission(organization, "PersonnelSignature.Create", "Create personnel signature");
        EnsurePermission(organization, "Position.Read", "Read positions");
        EnsurePermission(organization, "Position.Create", "Create positions");
        EnsurePermission(organization, "Position.Edit", "Edit positions");
        EnsurePermission(organization, "Position.Delete", "Delete positions");
        EnsurePermission(organization, "Chart.Read", "Read org chart");

        var accessMgmt = EnsureResource(qcApp, "AccessManagement", "Access Management", "Access control management");
        EnsurePermission(accessMgmt, "Role.Read", "Read roles");
        EnsurePermission(accessMgmt, "Role.Create", "Create roles");
        EnsurePermission(accessMgmt, "Role.Edit", "Edit roles");
        EnsurePermission(accessMgmt, "Role.Delete", "Delete roles");
        EnsurePermission(accessMgmt, "Role.Permissions.Manage", "Manage role permissions");
        EnsurePermission(accessMgmt, "Role.BulkAssign", "Bulk assign roles");
        EnsurePermission(accessMgmt, "Permission.Assign", "Assign permissions");
        EnsurePermission(accessMgmt, "RuleScope.Read", "Read rule scopes");
        EnsurePermission(accessMgmt, "Resource.Read", "Read resources");
        EnsurePermission(accessMgmt, "Role.Unassign", "Remove a role from a user");
        EnsurePermission(accessMgmt, "Delegation.Create", "Create delegated access rules");
        EnsurePermission(accessMgmt, "Delegation.Revoke", "Revoke delegated access rules");
        EnsurePermission(accessMgmt, "AuditLog.Read", "Read audit logs");

        var attachment = EnsureResource(qcApp, "Attachment", "Attachment", "Position and role attachments");
        EnsurePermission(attachment, "Create", "Upload attachments");
        EnsurePermission(attachment, "Read", "Read attachments");
        EnsurePermission(attachment, "Delete", "Delete attachments");

        // Persist new resources/permissions so implication targets have stable ids.
        await _context.SaveChangesAsync();

        EnsureImplication(products, "Edit", "Read");
        EnsureImplication(laboratory, "Approve", "Read");
        EnsureImplication(ncr, "Approve", "Read");
        EnsureImplication(organization, "Personnel.Edit", "Personnel.Read");
        EnsureImplication(organization, "Personnel.Delete", "Personnel.Read");
        EnsureImplication(organization, "Position.Edit", "Position.Read");
        EnsureImplication(organization, "Position.Delete", "Position.Read");
        EnsureImplication(organization, "PersonnelPosition.Edit", "PersonnelPosition.Create");
        EnsureImplication(organization, "PersonnelPosition.Delete", "PersonnelPosition.Create");
        EnsureImplication(organization, "PersonnelPosition.Correct", "PersonnelPosition.Create");
        EnsureImplication(accessMgmt, "Role.Permissions.Manage", "Role.Read");
        EnsureImplication(accessMgmt, "Role.BulkAssign", "Role.Read");
        EnsureImplication(accessMgmt, "Role.Unassign", "Role.Read");
        EnsureImplication(accessMgmt, "Role.Create", "Role.Read");
        EnsureImplication(accessMgmt, "Role.Edit", "Role.Read");
        EnsureImplication(accessMgmt, "Role.Delete", "Role.Read");
        EnsureImplication(accessMgmt, "Permission.Assign", "Role.Read");
        EnsureImplication(accessMgmt, "RuleScope.Read", "Resource.Read");
        EnsureImplication(attachment, "Delete", "Read");

        await _context.SaveChangesAsync();
    }

    private static Resource EnsureResource(Domain.AccessControl.Application application, string code, string name, string? description)
    {
        var existing = application.Resources.FirstOrDefault(r => r.Code == code);
        if (existing is not null)
        {
            existing.UpdateDetails(code, name, description, existing.SortOrder);
            return existing;
        }

        return application.AddResource(code, name, description);
    }

    private static Permission EnsurePermission(Resource resource, string actionCode, string description)
    {
        var existing = resource.Permissions.FirstOrDefault(p => p.ActionCode == actionCode);
        if (existing is not null)
        {
            existing.UpdateDetails(actionCode, description);
            return existing;
        }

        return resource.AddPermission(actionCode, description);
    }

    private static void EnsureImplication(Resource resource, string fromAction, string toAction)
    {
        var from = resource.Permissions.FirstOrDefault(p => p.ActionCode == fromAction);
        var to = resource.Permissions.FirstOrDefault(p => p.ActionCode == toAction);
        if (from is null || to is null)
            return;

        from.AddImplication(to.Id);
    }
}
