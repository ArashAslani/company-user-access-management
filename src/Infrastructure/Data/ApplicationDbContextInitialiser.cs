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
                await _userManager.AddToRolesAsync(administrator, new [] { administratorRole.Name });
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
        var personnelResource = organizationResource.AddPermission("Personnel.Read", "Read personnel");
        organizationResource.AddPermission("Personnel.Manage", "Manage personnel");
        organizationResource.AddPermission("Position.Manage", "Manage positions");
        organizationResource.AddPermission("Signature.Replace", "Replace signatures");
        organizationResource.AddPermission("Chart.Read", "Read org chart");

        var accessMgmtResource = qcApp.AddResource("AccessManagement", "Access Management", "Access control management");
        accessMgmtResource.AddPermission("Role.Manage", "Manage roles");
        accessMgmtResource.AddPermission("Permission.Assign", "Assign permissions");
        accessMgmtResource.AddPermission("Audit.Read", "Read audit logs");

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

        await _context.SaveChangesAsync();
    }
}