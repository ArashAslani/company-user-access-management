using CompanyAccessManagement.Application.Common.Interfaces;

namespace CompanyAccessManagement.Web.Services;

public class CurrentWorkspace : ICurrentWorkspace
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentWorkspace(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? CompanyId
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User?.FindFirst("company_id");
            if (claim != null && Guid.TryParse(claim.Value, out var companyId))
            {
                return companyId;
            }
            return null;
        }
    }
}
