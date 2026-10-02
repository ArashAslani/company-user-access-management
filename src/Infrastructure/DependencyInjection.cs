using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Audit;
using CompanyAccessManagement.Infrastructure.Audit;
using CompanyAccessManagement.Infrastructure.Authorization;
using CompanyAccessManagement.Infrastructure.Data;
using CompanyAccessManagement.Infrastructure.Data.Interceptors;
using CompanyAccessManagement.Infrastructure.Identity;
using CompanyAccessManagement.Infrastructure.Organization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static void AddInfrastructureServices(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(Services.Database);
        Guard.Against.Null(connectionString, message: $"Connection string '{Services.Database}' not found.");

        builder.Services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
        builder.Services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();
        builder.Services.AddScoped<ISaveChangesInterceptor, AuthorizationRevisionInterceptor>();

        builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
            options.UseSqlite(connectionString);
        });

        builder.Services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        builder.Services.AddScoped<ApplicationDbContextInitialiser>();
        builder.Services.AddScoped<DemoDataSeeder>();

        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<IAccessEvaluator, AccessEvaluator>();
        builder.Services.AddScoped<IAdminAuthority, AdminAuthority>();
        builder.Services.AddScoped<IAuditOperation, AuditOperation>();
        builder.Services.AddScoped<IAuditWriter, AuditWriter>();
        builder.Services.AddScoped<IDelegationAuthority, DelegationAuthority>();
        builder.Services.AddScoped<IExternalOrganizationResolver, ExternalOrganizationResolver>();
        builder.Services.AddSingleton<ISignatureImageValidator, ImageSharpSignatureValidator>();

        builder.Services.AddAuthentication(IdentityConstants.BearerScheme)
            .AddBearerToken(IdentityConstants.BearerScheme);

        builder.Services.AddAuthorizationBuilder();

        builder.Services
            .AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddApiEndpoints()
            .AddSignInManager<ApplicationSignInManager>();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddTransient<IIdentityService, IdentityService>();
    }
}

