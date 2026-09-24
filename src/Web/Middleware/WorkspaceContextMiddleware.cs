using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CompanyAccessManagement.Web.Middleware;

public class WorkspaceContextMiddleware
{
    private readonly RequestDelegate _next;

    public WorkspaceContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentWorkspace currentWorkspace, IApplicationDbContext dbContext)
    {
        // Extract company ID from header or query string
        var companyIdHeader = context.Request.Headers["X-Company-Id"].FirstOrDefault();
        if (Guid.TryParse(companyIdHeader, out var companyId))
        {
            // Validate membership
            var userIdClaim = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
            {
                var userCompany = await dbContext.UserCompanies
                    .FirstOrDefaultAsync(uc => uc.UserId == userId && uc.CompanyId == companyId && uc.Status == CompanyAccessManagement.Domain.AccessControl.UserCompanyStatus.Active);

                if (userCompany != null)
                {
                    // Set the company context - this would need ICurrentWorkspace to be settable
                    // For now, we rely on the CurrentWorkspace reading from claims
                }
            }
        }

        await _next(context);
    }
}
