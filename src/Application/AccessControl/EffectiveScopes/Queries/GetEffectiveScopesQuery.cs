using System.Text.Json.Serialization;
using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Application.Common.Security;
using CompanyAccessManagement.Domain.AccessControl;

namespace CompanyAccessManagement.Application.AccessControl.EffectiveScopes.Queries;

public sealed record GetEffectiveScopesQuery(string ApplicationCode, string PermissionCode, string ScopeType) : IRequest<EffectiveScopesDto>;

public sealed record EffectiveScopesDto(
    string ScopeMode,
    string ScopeType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? ScopeKeys,
    string Revision);

public sealed class GetEffectiveScopesQueryValidator : AbstractValidator<GetEffectiveScopesQuery>
{
    public GetEffectiveScopesQueryValidator()
    {
        RuleFor(x => x.ApplicationCode).NotEmpty();
        RuleFor(x => x.PermissionCode).NotEmpty();
        RuleFor(x => x.ScopeType).NotEmpty();
    }
}

public sealed class GetEffectiveScopesQueryHandler : IRequestHandler<GetEffectiveScopesQuery, EffectiveScopesDto>
{
    private readonly IAccessEvaluator _evaluator;
    private readonly ICurrentWorkspace _workspace;
    private readonly IUser _user;

    public GetEffectiveScopesQueryHandler(IAccessEvaluator evaluator, ICurrentWorkspace workspace, IUser user)
    {
        _evaluator = evaluator;
        _workspace = workspace;
        _user = user;
    }

    public async Task<EffectiveScopesDto> Handle(GetEffectiveScopesQuery request, CancellationToken cancellationToken)
    {
        if (_user.Id is not Guid userId)
            throw new UnauthorizedAccessException();

        var companyId = _workspace.RequireCompanyId();
        var result = await _evaluator.EvaluateEffectiveScopesAsync(
            new EffectiveScopesRequest(userId, companyId, request.ApplicationCode, request.PermissionCode, request.ScopeType),
            cancellationToken);

        return new EffectiveScopesDto(
            result.ScopeMode.ToString(),
            result.ScopeType,
            result.ScopeMode == ScopeMode.Selected ? result.Keys : null,
            result.Revision);
    }
}
