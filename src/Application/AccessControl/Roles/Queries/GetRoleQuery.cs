#pragma warning disable CS8602
#pragma warning disable CS8632

using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.AccessControl.Roles.Queries;
using CleanArchitecture.Domain.AccessControl;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.AccessControl.Roles.Queries;

public record GetRoleQuery : IRequest<RoleDetailDto?>
{
    public Guid Id { get; init; }
}

public class GetRoleQueryHandler : IRequestHandler<GetRoleQuery, RoleDetailDto?>
{
    private readonly IApplicationDbContext _context;

    public GetRoleQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<RoleDetailDto?> Handle(GetRoleQuery request, CancellationToken cancellationToken)
    {
        var role = await _context.Roles
            .Include(r => r.ParentRole)
            .Include(r => r.Children)
            .Include(r => r.UserRoles)
                .ThenInclude(ur => ur.UserCompany)
            .Include(r => r.AccessRules)
                .ThenInclude(ar => ar.Permission)
                    .ThenInclude(p => p.Resource)
            .Include(r => r.AccessRules)
                .ThenInclude(ar => ar.Scopes)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (role is null)
            return null;

        var r = role!;

        var dto = new RoleDetailDto
        {
            Id = r.Id,
            Code = r.Code,
            Title = r.Name,
            Description = r.Description ?? string.Empty,
            Kind = r.Kind,
            Status = r.Status,
            ValidUntil = r.ValidUntil,
            ParentRoleId = r.ParentRoleId,
            ParentRoleTitle = r.ParentRole?.Name,
            Children = r.Children.Select(c => new RoleDto
            {
                Id = c.Id,
                Code = c.Code,
                Title = c.Name,
                UserCount = c.UserRoles.Count,
                Status = c.Status
            }).ToList(),
            Users = r.UserRoles.Select(ur => new UserDto
            {
                UserId = ur.UserCompany?.UserId ?? Guid.Empty,
                FullName = ur.UserCompany?.UserId.ToString() ?? string.Empty
            }).ToList(),
            Permissions = r.AccessRules
                .Where(ar => ar.Effect == AccessEffect.Allow)
                .Select(ar => new PermissionDto
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

        return dto;
    }
}