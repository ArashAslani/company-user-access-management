using CompanyAccessManagement.Application.Common.Exceptions;
using CompanyAccessManagement.Application.Common.Hierarchy;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Common;

namespace CompanyAccessManagement.Application.AccessControl.Roles.Commands;

public record CreateRoleCommand : IRequest<Guid>
{
    public Guid CompanyId { get; init; }
    public Guid ApplicationId { get; init; }
    public string Name { get; init; } = null!;
    public string Code { get; init; } = null!;
    public string? Description { get; init; }
    public Guid? ParentRoleId { get; init; }
    public DateTime? ValidFrom { get; init; }
    public DateTime? ValidUntil { get; init; }
    public RoleKind Kind { get; init; } = RoleKind.Standard;
    public RoleStatus Status { get; init; } = RoleStatus.Active;
}

public class CreateRoleCommandHandler : IRequestHandler<CreateRoleCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;

    public CreateRoleCommandHandler(IApplicationDbContext context, ICurrentWorkspace workspace)
    {
        _context = context;
        _workspace = workspace;
    }

    public async Task<Guid> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.EnsureCompany(request.CompanyId);

        // Super-admin roles are provisioned out of band, never through the API.
        if (request.Kind != RoleKind.Standard)
            throw new ForbiddenAccessException();

        var applicationExists = await _context.Applications
            .AnyAsync(a => a.Id == request.ApplicationId, cancellationToken);
        if (!applicationExists)
            throw new NotFoundException(request.ApplicationId.ToString(), "Application");

        if (request.ParentRoleId.HasValue)
        {
            var parent = await _context.Roles
                .FirstOrDefaultAsync(r => r.Id == request.ParentRoleId.Value && r.CompanyId == companyId, cancellationToken);

            Guard.Against.NotFound(request.ParentRoleId.Value, parent);

            if (parent.ApplicationId != request.ApplicationId)
                throw new DomainRuleViolationException("ROLE_PARENT_APPLICATION_MISMATCH", "Parent role must belong to the same application.");

            // Detect an existing cycle on the proposed parent chain (new node has no id in the graph yet).
            await HierarchyCycle.EnsureAcyclicAsync(
                Guid.Empty,
                request.ParentRoleId,
                async (id, ct) => await _context.Roles
                    .Where(r => r.Id == id && r.CompanyId == companyId && r.ApplicationId == request.ApplicationId)
                    .Select(r => r.ParentRoleId)
                    .FirstOrDefaultAsync(ct),
                cancellationToken,
                "Role");
        }

        var codeExists = await _context.Roles
            .AnyAsync(r => r.CompanyId == companyId && r.ApplicationId == request.ApplicationId && r.Code == request.Code, cancellationToken);

        if (codeExists)
            throw new DomainRuleViolationException("ROLE_CODE_DUPLICATE", "Role code must be unique within the company and application.");

        var role = new Role(companyId, request.ApplicationId, request.Code, request.Name, RoleKind.Standard, request.ParentRoleId, request.ValidUntil);
        role.UpdateCodeAndDescription(request.Code, request.Description);
        role.SetStatus(request.Status);

        _context.Roles.Add(role);
        _context.AuthPrincipals.Add(AuthPrincipal.ForRole(role.Id, role.CompanyId, role.ApplicationId));
        await _context.SaveChangesAsync(cancellationToken);

        return role.Id;
    }
}
