using CompanyAccessManagement.Application.Common.Audit;
using CompanyAccessManagement.Application.Common.Exceptions;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;

namespace CompanyAccessManagement.Application.AccessControl.Delegations.Commands;

public record CreateDelegationCommand : IRequest<Guid>
{
    public Guid ToUserId { get; init; }
    public string PermissionCode { get; init; } = null!;
    public ScopeMode ScopeMode { get; init; }
    public string? ScopeType { get; init; }
    public string[]? ScopeKeys { get; init; }
    public DateTime? ValidUntil { get; init; }
}

public sealed class CreateDelegationCommandValidator : AbstractValidator<CreateDelegationCommand>
{
    public CreateDelegationCommandValidator()
    {
        RuleFor(x => x.ToUserId).NotEmpty();
        RuleFor(x => x.PermissionCode).NotEmpty();
        RuleFor(x => x.ScopeMode).IsInEnum();
        RuleFor(x => x.ScopeType).NotEmpty().When(x => x.ScopeMode == ScopeMode.Selected);
        RuleFor(x => x.ScopeKeys).NotEmpty().When(x => x.ScopeMode == ScopeMode.Selected);
        RuleFor(x => x.ScopeType).Empty().When(x => x.ScopeMode != ScopeMode.Selected);
        RuleFor(x => x.ScopeKeys).Empty().When(x => x.ScopeMode != ScopeMode.Selected);
    }
}

public sealed class CreateDelegationCommandHandler : IRequestHandler<CreateDelegationCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentWorkspace _workspace;
    private readonly IUser _user;
    private readonly IDelegationAuthority _authority;
    private readonly IAuditWriter _audit;

    public CreateDelegationCommandHandler(
        IApplicationDbContext context,
        ICurrentWorkspace workspace,
        IUser user,
        IDelegationAuthority authority,
        IAuditWriter audit)
    {
        _context = context;
        _workspace = workspace;
        _user = user;
        _authority = authority;
        _audit = audit;
    }

    public async Task<Guid> Handle(CreateDelegationCommand request, CancellationToken cancellationToken)
    {
        var companyId = _workspace.RequireCompanyId();
        var delegatorId = _user.Id ?? throw new ForbiddenAccessException();
        if (request.ToUserId == delegatorId)
            throw new Common.Exceptions.ValidationException([new FluentValidation.Results.ValidationFailure(nameof(request.ToUserId), "A permission cannot be delegated to yourself.")]);

        var scopes = request.ScopeMode == ScopeMode.Selected
            ? request.ScopeKeys!.Distinct().Select(k => new DelegationScopeRequest(request.ScopeType!, k)).ToList()
            : [];

        var assessment = await _authority.AssessAsync(new DelegationAssessmentRequest(
            delegatorId,
            companyId,
            AccessControlApplication.Code,
            request.PermissionCode,
            request.ScopeMode,
            scopes,
            request.ValidUntil), cancellationToken);

        var membership = await _context.UserCompanies
            .FirstOrDefaultAsync(uc => uc.UserId == request.ToUserId
                && uc.CompanyId == companyId
                && uc.Status == UserCompanyStatus.Active, cancellationToken);
        if (membership is null)
            throw new NotFoundException(request.ToUserId.ToString(), "UserCompany");

        var principal = await _context.AuthPrincipals
            .FirstOrDefaultAsync(p => p.Type == PrincipalType.UserCompany
                && p.ReferenceId == membership.Id
                && p.CompanyId == companyId
                && p.ApplicationId == assessment.ApplicationId, cancellationToken);
        if (principal is null)
        {
            principal = AuthPrincipal.ForUserCompany(membership.Id, companyId, assessment.ApplicationId);
            membership.AssignPrincipal(principal.Id);
            _context.AuthPrincipals.Add(principal);
        }

        var rule = new AccessRule(
            principal.Id,
            assessment.PermissionId,
            AccessEffect.Allow,
            AccessRuleOrigin.Delegated,
            request.ScopeMode,
            validUntil: assessment.CappedValidUntil,
            delegatedFromUserId: delegatorId);
        foreach (var scope in scopes)
            rule.AddScope(scope.ScopeType, scope.ScopeKey);
        principal.AddAccessRule(rule);

        _audit.Write(new AuditWriteRequest(
            AuditEventTypes.DelegationCreated,
            nameof(AccessRule),
            rule.Id,
            AccessRuleSourceType.Delegated,
            AfterData: AuditJson.Serialize(new
            {
                delegatedFromUserId = delegatorId,
                toUserId = request.ToUserId,
                permissionCode = request.PermissionCode,
                scopeMode = request.ScopeMode.ToString(),
                scopes = scopes.Select(s => $"{s.ScopeType}:{s.ScopeKey}"),
                validUntil = assessment.CappedValidUntil
            }),
            TargetPrincipalId: principal.Id,
            PermissionId: assessment.PermissionId,
            ApplicationId: assessment.ApplicationId,
            CompanyId: companyId));

        await _context.SaveChangesAsync(cancellationToken);
        return rule.Id;
    }
}
