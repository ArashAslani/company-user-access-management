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
        
        if (!string.IsNullOrEmpty(companyIdHeader) && Guid.TryParse(companyIdHeader, out var companyId))
        {
            // Validate membership
            var userIdClaim = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
            {
                var userCompany = await dbContext.UserCompanies
                    .FirstOrDefaultAsync(uc => uc.UserId == userId && uc.CompanyId == companyId && uc.Status == CompanyAccessManagement.Domain.AccessControl.UserCompanyStatus.Active);

                if (userCompany != null)
                {
                    // Add company_id claim to the user's principal so CurrentWorkspace can read it
                    var claimsIdentity = context.User.Identity as System.Security.Claims.ClaimsIdentity;
                    if (claimsIdentity != null)
                    {
                        // Remove existing company_id claim if present
                        var existingClaim = claimsIdentity.FindFirst("company_id");
                        if (existingClaim != null)
                        {
                            claimsIdentity.RemoveClaim(existingClaim);
                        }
                        // Add new company_id claim
                        claimsIdentity.AddClaim(new System.Security.Claims.Claim("company_id", companyId.ToString()));
                    }
                }
            }
        }

        await _next(context);
    }
}
