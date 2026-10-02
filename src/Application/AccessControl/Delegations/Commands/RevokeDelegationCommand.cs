using CompanyAccessManagement.Application.Common.Audit;
using CompanyAccessManagement.Application.Common.Exceptions;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;

namespace CompanyAccessManagement.Application.AccessControl.Delegations.Commands;

public sealed record RevokeDelegationCommand(Guid RuleId) : IRequest;

public sealed class RevokeDelegationCommandHandler : IRequestHandler<RevokeDelegationCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;
    private readonly IUser _user;
    private readonly IAuditWriter _audit;

    public RevokeDelegationCommandHandler(
        IApplicationDbContext context,
        ICurrentWorkspace workspace,
        IUser user,
        IAuditWriter audit)
    {
        _context = context;
        _workspace = workspace;
        _user = user;
        _audit = audit;
    }

    public async Task Handle(RevokeDelegationCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();
        var actorId = _user.Id ?? throw new ForbiddenAccessException();
        var rule = await _context.AccessRules
            .Include(r => r.Principal)
            .FirstOrDefaultAsync(r => r.Id == request.RuleId
                && r.Origin == AccessRuleOrigin.Delegated
                && r.DelegatedFromUserId == actorId
                && r.Principal != null
                && r.Principal.CompanyId == companyId, cancellationToken);
        Guard.Against.NotFound(request.RuleId, rule);

        rule.SetStatus(AccessRuleStatus.Inactive);
        _audit.Write(new AuditWriteRequest(
            AuditEventTypes.DelegationRevoked,
            nameof(AccessRule),
            rule.Id,
            AccessRuleSourceType.Delegated,
            TargetPrincipalId: rule.PrincipalId,
            PermissionId: rule.PermissionId,
            ApplicationId: rule.Principal!.ApplicationId,
            CompanyId: companyId));
        await _context.SaveChangesAsync(cancellationToken);
    }
}
