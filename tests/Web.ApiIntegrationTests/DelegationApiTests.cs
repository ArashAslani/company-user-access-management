using CompanyAccessManagement.Application.Common.Audit;
using CompanyAccessManagement.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Shouldly;
using System.Net;
using System.Net.Http.Json;

namespace CompanyAccessManagement.ApiIntegrationTests;

/// <summary>
/// Delegation write API (design §33, ADR-0008): the delegator must currently hold the permission through a non-delegated
/// grant, the delegated scope must be a subset of that grant, the delegation cannot outlive its source, and delegated
/// permissions cannot be delegated again.
/// </summary>
[TestFixture]
public class DelegationApiTests : ApiTestBase
{
    private const string DelegationsUrl = "/api/v1/access-control/delegations";
    private const string DelegateCreate = "AccessManagement.Delegation.Create";
    private const string DelegateRevoke = "AccessManagement.Delegation.Revoke";

    private Guid _companyId;
    private Guid _productsDeleteId;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        (_companyId, _, _, _, _productsDeleteId) = await SeedTestDataAsync();
    }

    [Test]
    public async Task Create_DelegatorHoldsPermission_DelegateGainsAccessAndAuditIsWritten()
    {
        var delegator = await CreateAuthorizedClientAsync("delegator@test.com", _companyId, DelegateCreate, "Organization.Position.Read");
        var delegate_ = await CreateAuthorizedClientAsync("delegate@test.com", _companyId);
        (await delegate_.Client.GetAsync("/api/v1/organization/positions/")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var response = await DelegateAsync(delegator, delegate_.UserId, "Organization.Position.Read");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var ruleId = await IdOfAsync(response);
        var rule = await WithDbAsync(db => db.AccessRules.AsNoTracking().SingleAsync(r => r.Id == ruleId));
        rule.Origin.ShouldBe(AccessRuleOrigin.Delegated);
        rule.DelegatedFromUserId.ShouldBe(delegator.UserId);
        rule.PrincipalId.ShouldBe(delegate_.PrincipalId);
        (await delegate_.Client.GetAsync("/api/v1/organization/positions/")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var audit = await WithDbAsync(db => db.AuditLogs.AsNoTracking().SingleAsync(a => a.EventType == AuditEventTypes.DelegationCreated));
        audit.EntityId.ShouldBe(ruleId);
        audit.ActorUserId.ShouldBe(delegator.UserId);
        audit.CompanyId.ShouldBe(_companyId);
        audit.TargetPrincipalId.ShouldBe(delegate_.PrincipalId);
        audit.SourceType.ShouldBe(AccessRuleSourceType.Delegated);
    }

    [Test]
    public async Task Create_DelegatorDoesNotHoldPermission_Forbidden()
    {
        var delegator = await CreateAuthorizedClientAsync("delegator@test.com", _companyId, DelegateCreate);
        var delegate_ = await CreateAuthorizedClientAsync("delegate@test.com", _companyId);

        var response = await DelegateAsync(delegator, delegate_.UserId, "Organization.Position.Read");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await DelegatedRuleCountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Create_DelegatorPermissionDenied_Forbidden()
    {
        var delegator = await CreateAuthorizedClientAsync("delegator@test.com", _companyId, DelegateCreate, "Products.Delete");
        await CreateAccessRuleAsync(delegator.PrincipalId, _productsDeleteId, AccessEffect.Deny);
        var delegate_ = await CreateAuthorizedClientAsync("delegate@test.com", _companyId);

        var response = await DelegateAsync(delegator, delegate_.UserId, "Products.Delete");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await DelegatedRuleCountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Create_DelegatorWithSelectedScope_SubsetAllowed()
    {
        var delegator = await CreateAuthorizedClientAsync("delegator@test.com", _companyId, DelegateCreate);
        await AddRuleAsync(delegator.PrincipalId, ScopeMode.Selected, validUntil: null, "W1", "W2");
        var delegate_ = await CreateAuthorizedClientAsync("delegate@test.com", _companyId);

        var response = await DelegateAsync(delegator, delegate_.UserId, "Products.Delete", ScopeMode.Selected, ["W1"]);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var ruleId = await IdOfAsync(response);
        var scopes = await WithDbAsync(db => db.AccessRules.Include(r => r.Scopes).AsNoTracking()
            .Where(r => r.Id == ruleId).SelectMany(r => r.Scopes.Select(s => s.ScopeKey)).ToListAsync());
        scopes.ShouldBe(["W1"]);
    }

    [Test]
    public async Task Create_DelegatorWithSelectedScope_KeyOutsideGrantIsBadRequest()
    {
        var delegator = await CreateAuthorizedClientAsync("delegator@test.com", _companyId, DelegateCreate);
        await AddRuleAsync(delegator.PrincipalId, ScopeMode.Selected, validUntil: null, "W1");
        var delegate_ = await CreateAuthorizedClientAsync("delegate@test.com", _companyId);

        var response = await DelegateAsync(delegator, delegate_.UserId, "Products.Delete", ScopeMode.Selected, ["W1", "W9"]);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await DelegatedRuleCountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Create_DelegatorWithSelectedScope_RequestingAllIsBadRequest()
    {
        var delegator = await CreateAuthorizedClientAsync("delegator@test.com", _companyId, DelegateCreate);
        await AddRuleAsync(delegator.PrincipalId, ScopeMode.Selected, validUntil: null, "W1");
        var delegate_ = await CreateAuthorizedClientAsync("delegate@test.com", _companyId);

        var response = await DelegateAsync(delegator, delegate_.UserId, "Products.Delete", ScopeMode.All);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await DelegatedRuleCountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Create_DelegatorWithAllScope_AnySelectedScopeAllowed()
    {
        var delegator = await CreateAuthorizedClientAsync("delegator@test.com", _companyId, DelegateCreate);
        await AddRuleAsync(delegator.PrincipalId, ScopeMode.All, validUntil: null);
        var delegate_ = await CreateAuthorizedClientAsync("delegate@test.com", _companyId);

        var response = await DelegateAsync(delegator, delegate_.UserId, "Products.Delete", ScopeMode.Selected, ["ANY-WORKSHOP"]);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task Create_ValidUntilWithinSource_Allowed()
    {
        var sourceUntil = DateTime.UtcNow.AddDays(5);
        var delegator = await CreateAuthorizedClientAsync("delegator@test.com", _companyId, DelegateCreate);
        await AddRuleAsync(delegator.PrincipalId, ScopeMode.None, sourceUntil);
        var delegate_ = await CreateAuthorizedClientAsync("delegate@test.com", _companyId);

        var response = await DelegateAsync(delegator, delegate_.UserId, "Products.Delete", validUntil: sourceUntil.AddDays(-1));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task Create_ValidUntilBeyondSource_Conflict()
    {
        var sourceUntil = DateTime.UtcNow.AddDays(5);
        var delegator = await CreateAuthorizedClientAsync("delegator@test.com", _companyId, DelegateCreate);
        await AddRuleAsync(delegator.PrincipalId, ScopeMode.None, sourceUntil);
        var delegate_ = await CreateAuthorizedClientAsync("delegate@test.com", _companyId);

        var response = await DelegateAsync(delegator, delegate_.UserId, "Products.Delete", validUntil: sourceUntil.AddDays(1));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("DELEGATION_VALID_UNTIL_EXCEEDS_SOURCE");
        (await DelegatedRuleCountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Create_SourceWithoutValidUntil_AnyValidUntilAllowed()
    {
        var delegator = await CreateAuthorizedClientAsync("delegator@test.com", _companyId, DelegateCreate);
        await AddRuleAsync(delegator.PrincipalId, ScopeMode.None, validUntil: null);
        var delegate_ = await CreateAuthorizedClientAsync("delegate@test.com", _companyId);

        var response = await DelegateAsync(delegator, delegate_.UserId, "Products.Delete", validUntil: DateTime.UtcNow.AddYears(10));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task Create_PermissionHeldOnlyThroughDelegation_RedelegationConflict()
    {
        var owner = await CreateAuthorizedClientAsync("owner@test.com", _companyId, DelegateCreate, "Organization.Position.Read");
        var middle = await CreateAuthorizedClientAsync("middle@test.com", _companyId, DelegateCreate);
        var last = await CreateAuthorizedClientAsync("last@test.com", _companyId);
        (await DelegateAsync(owner, middle.UserId, "Organization.Position.Read")).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await DelegateAsync(middle, last.UserId, "Organization.Position.Read");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("REDELEGATION_FORBIDDEN");
        (await DelegatedRuleCountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task Create_ToSelf_BadRequest()
    {
        var delegator = await CreateAuthorizedClientAsync("delegator@test.com", _companyId, DelegateCreate, "Organization.Position.Read");

        var response = await DelegateAsync(delegator, delegator.UserId, "Organization.Position.Read");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await DelegatedRuleCountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Revoke_ByDelegator_DeactivatesRuleRemovesAccessAndAudits()
    {
        var delegator = await CreateAuthorizedClientAsync("delegator@test.com", _companyId, DelegateCreate, DelegateRevoke, "Organization.Position.Read");
        var delegate_ = await CreateAuthorizedClientAsync("delegate@test.com", _companyId);
        var ruleId = await IdOfAsync(await DelegateAsync(delegator, delegate_.UserId, "Organization.Position.Read"));

        var response = await delegator.Client.DeleteAsync($"{DelegationsUrl}/{ruleId}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await WithDbAsync(db => db.AccessRules.AsNoTracking().Where(r => r.Id == ruleId).Select(r => r.Status).SingleAsync()))
            .ShouldBe(AccessRuleStatus.Inactive);
        (await delegate_.Client.GetAsync("/api/v1/organization/positions/")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var audit = await WithDbAsync(db => db.AuditLogs.AsNoTracking().SingleAsync(a => a.EventType == AuditEventTypes.DelegationRevoked));
        audit.EntityId.ShouldBe(ruleId);
        audit.ActorUserId.ShouldBe(delegator.UserId);
    }

    [Test]
    public async Task Revoke_ByAnotherUser_NotFoundAndRuleStaysActive()
    {
        var delegator = await CreateAuthorizedClientAsync("delegator@test.com", _companyId, DelegateCreate, "Organization.Position.Read");
        var delegate_ = await CreateAuthorizedClientAsync("delegate@test.com", _companyId);
        var other = await CreateAuthorizedClientAsync("other@test.com", _companyId, DelegateRevoke);
        var ruleId = await IdOfAsync(await DelegateAsync(delegator, delegate_.UserId, "Organization.Position.Read"));

        var response = await other.Client.DeleteAsync($"{DelegationsUrl}/{ruleId}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await WithDbAsync(db => db.AccessRules.AsNoTracking().Where(r => r.Id == ruleId).Select(r => r.Status).SingleAsync()))
            .ShouldBe(AccessRuleStatus.Active);
    }

    private static Task<HttpResponseMessage> DelegateAsync(AuthSession delegator, Guid toUserId, string permissionCode,
        ScopeMode scopeMode = ScopeMode.None, string[]? scopeKeys = null, DateTime? validUntil = null)
        => delegator.Client.PostAsJsonAsync(DelegationsUrl, new
        {
            ToUserId = toUserId,
            PermissionCode = permissionCode,
            ScopeMode = scopeMode,
            ScopeType = scopeMode == ScopeMode.Selected ? "Workshop" : null,
            ScopeKeys = scopeKeys,
            ValidUntil = validUntil
        });

    private Task AddRuleAsync(Guid principalId, ScopeMode scopeMode, DateTime? validUntil, params string[] workshopKeys)
        => WithDbAsync(async db =>
        {
            var rule = new AccessRule(principalId, _productsDeleteId, AccessEffect.Allow, AccessRuleOrigin.Manual, scopeMode, validUntil: validUntil);
            foreach (var key in workshopKeys)
                rule.AddScope("Workshop", key);
            db.AccessRules.Add(rule);
            await db.SaveChangesAsync(default);
        });

    private Task<int> DelegatedRuleCountAsync()
        => WithDbAsync(db => db.AccessRules.CountAsync(r => r.Origin == AccessRuleOrigin.Delegated));

    private static async Task<Guid> IdOfAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;

    private sealed record IdResponse(Guid Id);
}
