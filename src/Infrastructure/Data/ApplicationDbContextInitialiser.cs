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

        // Add permission implications: Edit => Read
        var editPerm = productsResource.Permissions.First(p => p.ActionCode == "Edit");
        var readPerm = productsResource.Permissions.First(p => p.ActionCode == "Read");
        editPerm.AddImplication(readPerm.Id);

        var labApprove = laboratoryResource.Permissions.First(p => p.ActionCode == "Approve");
        var labRead = laboratoryResource.Permissions.First(p => p.ActionCode == "Read");
        labApprove.AddImplication(labRead.Id);

        var ncrApprove = ncrResource.Permissions.First(p => p.ActionCode == "Approve");
        var ncrRead = ncrResource.Permissions.First(p => p.ActionCode == "Read");
        ncrApprove.AddImplication(ncrRead.Id);

        // Personnel.Edit requires Personnel.Read
        var personnelEdit = organizationResource.Permissions.First(p => p.ActionCode == "Personnel.Edit");
        var personnelRead = organizationResource.Permissions.First(p => p.ActionCode == "Personnel.Read");
        personnelEdit.AddImplication(personnelRead.Id);

        // Personnel.Delete requires Personnel.Read
        var personnelDelete = organizationResource.Permissions.First(p => p.ActionCode == "Personnel.Delete");
        personnelDelete.AddImplication(personnelRead.Id);

        // Position.Edit requires Position.Read
        var positionEdit = organizationResource.Permissions.First(p => p.ActionCode == "Position.Edit");
        var positionRead = organizationResource.Permissions.First(p => p.ActionCode == "Position.Read");
        positionEdit.AddImplication(positionRead.Id);

        // Position.Delete requires Position.Read
        var positionDelete = organizationResource.Permissions.First(p => p.ActionCode == "Position.Delete");
        positionDelete.AddImplication(positionRead.Id);

        // PersonnelPosition.Edit requires PersonnelPosition.Create
        var ppEdit = organizationResource.Permissions.First(p => p.ActionCode == "PersonnelPosition.Edit");
        var ppCreate = organizationResource.Permissions.First(p => p.ActionCode == "PersonnelPosition.Create");
        ppEdit.AddImplication(ppCreate.Id);

        // PersonnelPosition.Delete requires PersonnelPosition.Create
        var ppDelete = organizationResource.Permissions.First(p => p.ActionCode == "PersonnelPosition.Delete");
        ppDelete.AddImplication(ppCreate.Id);

        // AuditLog.Export requires AuditLog.Read
        var auditExport = accessMgmtResource.Permissions.First(p => p.ActionCode == "AuditLog.Export");
        var auditRead = accessMgmtResource.Permissions.First(p => p.ActionCode == "AuditLog.Read");
        auditExport.AddImplication(auditRead.Id);

        // Role.Permissions.Manage requires Role.Read
        var rolePermManage = accessMgmtResource.Permissions.First(p => p.ActionCode == "Role.Permissions.Manage");
        var roleRead = accessMgmtResource.Permissions.First(p => p.ActionCode == "Role.Read");
        rolePermManage.AddImplication(roleRead.Id);

        // Role.BulkAssign requires Role.Read
        var roleBulkAssign = accessMgmtResource.Permissions.First(p => p.ActionCode == "Role.BulkAssign");
        roleBulkAssign.AddImplication(roleRead.Id);

        // Role.Create, Role.Edit, Role.Delete require Role.Read
        var roleCreate = accessMgmtResource.Permissions.First(p => p.ActionCode == "Role.Create");
        roleCreate.AddImplication(roleRead.Id);
        var roleEdit = accessMgmtResource.Permissions.First(p => p.ActionCode == "Role.Edit");
        roleEdit.AddImplication(roleRead.Id);
        var roleDelete = accessMgmtResource.Permissions.First(p => p.ActionCode == "Role.Delete");
        roleDelete.AddImplication(roleRead.Id);

        // Position.Create, Position.Edit, Position.Delete require Position.Read
        var posCreate = organizationResource.Permissions.First(p => p.ActionCode == "Position.Create");
        posCreate.AddImplication(positionRead.Id);
        positionEdit.AddImplication(positionRead.Id);
        positionDelete.AddImplication(positionRead.Id);

        // Personnel.Create, Personnel.Edit, Personnel.Delete require Personnel.Read
        var persCreate = organizationResource.Permissions.First(p => p.ActionCode == "Personnel.Create");
        persCreate.AddImplication(personnelRead.Id);
        personnelEdit.AddImplication(personnelRead.Id);
        personnelDelete.AddImplication(personnelRead.Id);

        // PersonnelPosition.Create, Edit, Delete chain
        ppEdit.AddImplication(ppCreate.Id);
        ppDelete.AddImplication(ppCreate.Id);

        // PersonnelSignature.Create requires Personnel.Read
        var sigCreate = organizationResource.Permissions.First(p => p.ActionCode == "PersonnelSignature.Create");
        sigCreate.AddImplication(personnelRead.Id);

        // Attachment.Delete requires Attachment.Create
        var attDelete = attachmentsResource.Permissions.First(p => p.ActionCode == "Attachment.Delete");
        var attCreate = attachmentsResource.Permissions.First(p => p.ActionCode == "Attachment.Create");
        attDelete.AddImplication(attCreate.Id);

        // Attachment.Read requires Attachment.Create
        var attRead = attachmentsResource.Permissions.First(p => p.ActionCode == "Attachment.Read");
        attRead.AddImplication(attCreate.Id);

        // RuleScope.Read requires Resource.Read
        var ruleScopeRead = accessMgmtResource.Permissions.First(p => p.ActionCode == "RuleScope.Read");
        var resourceRead = accessMgmtResource.Permissions.First(p => p.ActionCode == "Resource.Read");
        ruleScopeRead.AddImplication(resourceRead.Id);

        // AccessManagement.Permission.Assign requires Role.Read
        var permAssign = accessMgmtResource.Permissions.First(p => p.ActionCode == "Permission.Assign");
        permAssign.AddImplication(roleRead.Id);

        // AccessManagement.Role.Manage requires Role.Read
        var roleManage = accessMgmtResource.Permissions.First(p => p.ActionCode == "Role.Manage");
        roleManage.AddImplication(roleRead.Id);

        await _context.SaveChangesAsync();
    }
}