using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;

namespace CompanyAccessManagement.Application.AccessControl.Roles.Queries;

public record GetRoleQuery : IRequest<RoleDetailDto?>
{
    public Guid Id { get; init; }
}

public class GetRoleQueryHandler : IRequestHandler<GetRoleQuery, RoleDetailDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;

    public GetRoleQueryHandler(IApplicationDbContext context, ICurrentWorkspace workspace)
    {
        _context = context;
        _workspace = workspace;
    }

    public async Task<RoleDetailDto?> Handle(GetRoleQuery request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        var role = await _context.Roles
            .AsNoTracking()
            .Include(r => r.ParentRole)
            .Include(r => r.Children)
                .ThenInclude(c => c.UserRoles)
            .Include(r => r.UserRoles)
                .ThenInclude(ur => ur.UserCompany)
            .InAccessControlApplication(_context)
            .FirstOrDefaultAsync(r => r.Id == request.Id && r.CompanyId == companyId, cancellationToken);

        if (role is null)
            return null;

        var rules = await (from ap in _context.AuthPrincipals
                           join ar in _context.AccessRules on ap.Id equals ar.PrincipalId
                           where ap.Type == PrincipalType.Role && ap.ReferenceId == role.Id
                               && ap.CompanyId == role.CompanyId && ap.ApplicationId == role.ApplicationId
                           select ar)
            .AsNoTracking()
            .Include(ar => ar.Scopes)
            .Include(ar => ar.Permission)
                .ThenInclude(p => p!.Resource)
            .ToListAsync(cancellationToken);

        return new RoleDetailDto
        {
            Id = role.Id,
            Code = role.Code,
            Title = role.Name,
            Description = role.Description ?? string.Empty,
            Kind = role.Kind,
            Status = role.Status,
            ValidUntil = role.ValidUntil,
            ParentRoleId = role.ParentRoleId,
            ParentRoleTitle = role.ParentRole?.Name,
            Children = role.Children.Select(c => new RoleDto
            {
                Id = c.Id,
                Code = c.Code,
                Title = c.Name,
                UserCount = c.UserRoles.Count,
                Status = c.Status
            }).ToList(),
            Users = role.UserRoles.Select(ur => new UserDto
            {
                UserId = ur.UserCompany?.UserId ?? Guid.Empty,
                FullName = ur.UserCompany?.UserId.ToString() ?? string.Empty
            }).ToList(),
            Permissions = rules.Select(ar => new PermissionDto
            {
                ResourceId = ar.Permission?.ResourceId ?? Guid.Empty,
                ResourceName = ar.Permission?.Resource?.Name ?? string.Empty,
                ActionCode = ar.Permission?.ActionCode ?? string.Empty,
                Effect = ar.Effect,
                ScopeMode = ar.ScopeMode.ToString(),
                Scopes = ar.Scopes.Select(s => new ScopeDto
                {
                    ScopeType = s.ScopeType,
                    ScopeKey = s.ScopeKey
                }).ToList()
            }).ToList()
        };
    }
}
