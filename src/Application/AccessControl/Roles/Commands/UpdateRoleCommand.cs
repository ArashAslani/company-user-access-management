using CompanyAccessManagement.Application.Common.Exceptions;
using CompanyAccessManagement.Application.Common.Hierarchy;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Common;

namespace CompanyAccessManagement.Application.AccessControl.Roles.Commands;

public record UpdateRoleCommand : IRequest
{
    public Guid Id { get; set; }
    public string Name { get; init; } = null!;
    public string Code { get; init; } = null!;
    public string? Description { get; init; }
    public RoleKind Kind { get; init; }
    public Guid? ParentRoleId { get; init; }
    public DateTime? ValidUntil { get; init; }
    public RoleStatus Status { get; init; }
}

public class UpdateRoleCommandHandler : IRequestHandler<UpdateRoleCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;

    public UpdateRoleCommandHandler(IApplicationDbContext context, ICurrentWorkspace workspace)
    {
        _context = context;
        _workspace = workspace;
    }

    public async Task Handle(UpdateRoleCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();

        var role = await _context.Roles
            .InAccessControlApplication(_context)
            .FirstOrDefaultAsync(r => r.Id == request.Id && r.CompanyId == companyId, cancellationToken);

        Guard.Against.NotFound(request.Id, role);

        // Super-admin roles cannot be edited, and no role can be escalated to one, through the API.
        if (role.Kind != RoleKind.Standard || request.Kind != RoleKind.Standard)
            throw new ForbiddenAccessException();

        // Read the company revision before the hierarchy snapshot: a concurrent parent change commits a newer
        // revision, so this save fails instead of both writers passing the cycle check on stale reads.
        if (request.ParentRoleId != role.ParentRoleId)
        {
            var company = await _context.Companies.SingleAsync(c => c.Id == role.CompanyId, cancellationToken);
            company.TouchAuthorization();
        }

        if (request.ParentRoleId.HasValue)
        {
            var parent = await _context.Roles
                .FirstOrDefaultAsync(r => r.Id == request.ParentRoleId.Value && r.CompanyId == companyId, cancellationToken);

            Guard.Against.NotFound(request.ParentRoleId.Value, parent);

            if (parent.ApplicationId != role.ApplicationId)
                throw new DomainRuleViolationException("ROLE_PARENT_APPLICATION_MISMATCH", "Parent role must belong to the same application.");

            await HierarchyCycle.EnsureAcyclicAsync(
                role.Id,
                request.ParentRoleId,
                async (id, ct) => await _context.Roles
                    .Where(r => r.Id == id && r.CompanyId == companyId && r.ApplicationId == role.ApplicationId)
                    .Select(r => r.ParentRoleId)
                    .FirstOrDefaultAsync(ct),
                cancellationToken,
                "Role");
        }

        var codeExists = await _context.Roles
            .AnyAsync(r => r.CompanyId == companyId && r.ApplicationId == role.ApplicationId && r.Code == request.Code && r.Id != request.Id, cancellationToken);

        if (codeExists)
            throw new DomainRuleViolationException("ROLE_CODE_DUPLICATE", "Role code must be unique within the company and application.");

        role.UpdateDetails(request.Name, request.ValidUntil);
        role.UpdateCodeAndDescription(request.Code, request.Description);

        if (request.ParentRoleId != role.ParentRoleId)
            role.ChangeParent(request.ParentRoleId);

        role.SetStatus(request.Status);

        await _context.SaveChangesAsync(cancellationToken);
    }
}
