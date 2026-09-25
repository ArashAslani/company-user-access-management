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
    }
}

public class ApplicationDbContextInitialiser
{
    private readonly ILogger<ApplicationDbContextInitialiser> _logger;
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;

    public ApplicationDbContextInitialiser(ILogger<ApplicationDbContextInitialiser> logger, ApplicationDbContext context, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<Guid>> roleManager)
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
            // Use migrations instead of EnsureDeleted/EnsureCreated
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
        // Default Identity roles (for authentication only, not business roles)
        var administratorRole = new IdentityRole<Guid>(Roles.Administrator);

        if (_roleManager.Roles.All(r => r.Name != administratorRole.Name))
        {
            await _roleManager.CreateAsync(administratorRole);
        }

        // Default users
        var administrator = new ApplicationUser { UserName = "administrator@localhost", Email = "administrator@localhost", IsActive = true };

        if (_userManager.Users.All(u => u.UserName != administrator.UserName))
        {
            await _userManager.CreateAsync(administrator, "Administrator1!");
            if (!string.IsNullOrWhiteSpace(administratorRole.Name))
            {
                await _userManager.AddToRolesAsync(administrator, new[] { administratorRole.Name });
            }
        }

        // Seed QC Application
        var qcApp = new CompanyAccessManagement.Domain.AccessControl.Application("QC", "Quality Control", "Quality Control Application");
        _context.Applications.Add(qcApp);

        // Seed Resources and Permissions for QC
        var productsResource = qcApp.AddResource("Products", "Products", "Product management");
        productsResource.AddPermission("Read", "Read products");
        productsResource.AddPermission("Edit", "Edit products");
        productsResource.AddPermission("Delete", "Delete products");

        var laboratoryResource = qcApp.AddResource("Laboratory", "Laboratory", "Laboratory management");
        laboratoryResource.AddPermission("Read", "Read laboratory data");
        laboratoryResource.AddPermission("Approve", "Approve laboratory results");

        var ncrResource = qcApp.AddResource("NCR", "Non-Conformance Reports", "NCR management");
        ncrResource.AddPermission("Read", "Read NCRs");
        ncrResource.AddPermission("Approve", "Approve NCRs");

        var organizationResource = qcApp.AddResource("Organization", "Organization", "Organization management");
        organizationResource.AddPermission("Personnel.Read", "Read personnel");
        organizationResource.AddPermission("Personnel.Create", "Create personnel");
        organizationResource.AddPermission("Personnel.Edit", "Edit personnel");
        organizationResource.AddPermission("Personnel.Delete", "Delete personnel");
        organizationResource.AddPermission("PersonnelPosition.Create", "Create personnel position assignment");
        organizationResource.AddPermission("PersonnelPosition.Edit", "Edit personnel position assignment");
        organizationResource.AddPermission("PersonnelPosition.Delete", "Delete personnel position assignment");
        organizationResource.AddPermission("PersonnelSignature.Create", "Create personnel signature");
        organizationResource.AddPermission("Position.Read", "Read positions");
        organizationResource.AddPermission("Position.Create", "Create positions");
        organizationResource.AddPermission("Position.Edit", "Edit positions");
        organizationResource.AddPermission("Position.Delete", "Delete positions");
        organizationResource.AddPermission("Chart.Read", "Read org chart");

        var accessMgmtResource = qcApp.AddResource("AccessManagement", "Access Management", "Access control management");
        accessMgmtResource.AddPermission("Role.Read", "Read roles");
        accessMgmtResource.AddPermission("Role.Create", "Create roles");
        accessMgmtResource.AddPermission("Role.Edit", "Edit roles");
        accessMgmtResource.AddPermission("Role.Delete", "Delete roles");
        accessMgmtResource.AddPermission("Role.Permissions.Manage", "Manage role permissions");
        accessMgmtResource.AddPermission("Role.BulkAssign", "Bulk assign roles");
        accessMgmtResource.AddPermission("Permission.Assign", "Assign permissions");
        accessMgmtResource.AddPermission("AuditLog.Read", "Read audit logs");
        accessMgmtResource.AddPermission("AuditLog.Export", "Export audit logs");
        accessMgmtResource.AddPermission("RuleScope.Read", "Read rule scopes");
        accessMgmtResource.AddPermission("Resource.Read", "Read resources");

        var attachmentsResource = qcApp.AddResource("Attachments", "Attachments", "Attachment management");
        attachmentsResource.AddPermission("Attachment.Create", "Create attachments");
        attachmentsResource.AddPermission("Attachment.Read", "Read attachments");
        attachmentsResource.AddPermission("Attachment.Delete", "Delete attachments");

        // Helper method to safely add implication if permission exists
        var safeAddImplication = (Resource resource, string fromAction, string toAction) =>
        {
            var fromPerm = resource.Permissions.FirstOrDefault(p => p.ActionCode == fromAction);
            var toPerm = resource.Permissions.FirstOrDefault(p => p.ActionCode == toAction);
            if (fromPerm != null && toPerm != null)
            {
                fromPerm.AddImplication(toPerm.Id);
            }
        };

        // Edit => Read implications
        safeAddImplication(productsResource, "Edit", "Read");
        safeAddImplication(laboratoryResource, "Approve", "Read");
        safeAddImplication(ncrResource, "Approve", "Read");

        // Personnel
        safeAddImplication(organizationResource, "Personnel.Edit", "Personnel.Read");
        safeAddImplication(organizationResource, "Personnel.Delete", "Personnel.Read");

        // Position
        safeAddImplication(organizationResource, "Position.Edit", "Position.Read");
        safeAddImplication(organizationResource, "Position.Delete", "Position.Read");

        // PersonnelPosition
        safeAddImplication(organizationResource, "PersonnelPosition.Edit", "PersonnelPosition.Create");
        safeAddImplication(organizationResource, "PersonnelPosition.Delete", "PersonnelPosition.Create");

        // AuditLog
        safeAddImplication(accessMgmtResource, "AuditLog.Export", "AuditLog.Read");

        // Role
        safeAddImplication(accessMgmtResource, "Role.Permissions.Manage", "Role.Read");
        safeAddImplication(accessMgmtResource, "Role.BulkAssign", "Role.Read");
        safeAddImplication(accessMgmtResource, "Role.Create", "Role.Read");
        safeAddImplication(accessMgmtResource, "Role.Edit", "Role.Read");
        safeAddImplication(accessMgmtResource, "Role.Delete", "Role.Read");

        // Permission
        safeAddImplication(accessMgmtResource, "Permission.Assign", "Role.Read");

        // Attachment
        safeAddImplication(attachmentsResource, "Attachment.Delete", "Attachment.Create");
        safeAddImplication(attachmentsResource, "Attachment.Read", "Attachment.Create");

        // RuleScope
        safeAddImplication(accessMgmtResource, "RuleScope.Read", "Resource.Read");

        await _context.SaveChangesAsync();
    }
}