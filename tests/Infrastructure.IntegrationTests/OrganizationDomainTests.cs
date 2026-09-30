using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.Common;
using CompanyAccessManagement.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyAccessManagement.IntegrationTests;

[TestFixture]
public class OrganizationDomainTests : TestBase
{
    private Guid _companyId;
    private Guid _positionId;
    private Guid _personnelId;
    private Personnel _personnel = null!;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        (_companyId, _, _, _, _) = await SeedTestDataAsync();

        // Create test personnel
        _personnel = new Personnel("1234567890", "John", "Doe", Gender.Male, "P001", "555-1234");
        Context.Personnel.Add(_personnel);
        await Context.SaveChangesAsync(default);
        _personnelId = _personnel.Id;

        // Create test position
        var position = new Position(_companyId, "DEV", "Developer", "Software Developer");
        Context.Positions.Add(position);
        await Context.SaveChangesAsync(default);
        _positionId = position.Id;
    }

    [Test]
    public async Task CreatePosition_SameCompanyParent_Succeeds()
    {
        var parent = new Position(_companyId, "MGR", "Manager", "Team Manager");
        Context.Positions.Add(parent);
        await Context.SaveChangesAsync(default);

        var child = new Position(_companyId, "SRDEV", "Senior Developer", "Senior Developer", parent.Id);
        Context.Positions.Add(child);
        await Context.SaveChangesAsync(default);

        var saved = await Context.Positions.FindAsync(child.Id);
        Assert.That(saved, Is.Not.Null);
        Assert.That(saved.ParentPositionId, Is.EqualTo(parent.Id));
    }

    [Test]
    public async Task CreatePosition_CrossCompanyParent_AllowedAtDomainLevel()
    {
        var otherCompany = new Company("OTHER", "Other Company");
        Context.Companies.Add(otherCompany);
        await Context.SaveChangesAsync(default);

        var parent = new Position(otherCompany.Id, "MGR", "Manager", "Team Manager");
        Context.Positions.Add(parent);
        await Context.SaveChangesAsync(default);

        // Cross-company parent validation is performed at application service layer
        // Domain entity allows creation; validation enforced at application service layer
        // Use a different code than the one created in SetUp ("DEV")
        var child = new Position(_companyId, "DEV2", "Developer", "Software Developer", parent.Id);
        Context.Positions.Add(child);
        await Context.SaveChangesAsync(default);

        var saved = await Context.Positions.FindAsync(child.Id);
        Assert.That(saved, Is.Not.Null);
        Assert.That(saved.ParentPositionId, Is.EqualTo(parent.Id));
    }

    [Test]
    public async Task CreatePosition_SelfParent_Fails()
    {
        var position = new Position(_companyId, "SELF", "Self Parent", "Self Parent Test");
        Context.Positions.Add(position);
        await Context.SaveChangesAsync(default);

        ShouldViolate(() => position.ChangeParent(position.Id), "HIERARCHY_CYCLE");
    }

    [Test]
    public async Task CreatePosition_CycleDetection_AllowedAtDomainLevel()
    {
        var posA = new Position(_companyId, "A", "Position A", "Position A");
        var posB = new Position(_companyId, "B", "Position B", "Position B", posA.Id);
        Context.Positions.AddRange(posA, posB);
        await Context.SaveChangesAsync(default);

        var posC = new Position(_companyId, "C", "Position C", "Position C", posB.Id);
        Context.Positions.Add(posC);
        await Context.SaveChangesAsync(default);

        // Cycle detection requires full hierarchy traversal - implemented at application service layer
        // Domain entity ChangeParent allows the change; cycle detection at application service layer
        posA.ChangeParent(posC.Id);
        await Context.SaveChangesAsync(default);

        // Domain entity allows the change; cycle detection enforced at application service layer
        var saved = await Context.Positions.FindAsync(posA.Id);
        Assert.That(saved!.ParentPositionId, Is.EqualTo(posC.Id));
    }

    [Test]
    public async Task CreatePosition_DuplicateCode_Fails()
    {
        var pos1 = new Position(_companyId, "DUP", "Position 1", "First");
        Context.Positions.Add(pos1);
        await Context.SaveChangesAsync(default);

        var pos2 = new Position(_companyId, "DUP", "Position 2", "Second");
        Context.Positions.Add(pos2);

        // SQLite enforces unique constraints, so this should throw
        var ex = Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(async () =>
            await Context.SaveChangesAsync(default));

        Assert.That(ex.InnerException, Is.InstanceOf<Microsoft.Data.Sqlite.SqliteException>());
        var sqliteEx = (Microsoft.Data.Sqlite.SqliteException)ex.InnerException!;
        Assert.That(sqliteEx.SqliteErrorCode, Is.EqualTo(19)); // UNIQUE constraint failed
    }

    private static DateTime Now => DateTime.UtcNow;

    private IReadOnlyDictionary<Guid, Guid> PositionCompanies() =>
        Context.Positions.AsNoTracking().ToDictionary(p => p.Id, p => p.CompanyId);

    private static void ShouldViolate(Action action, string code)
    {
        var ex = Should.Throw<DomainRuleViolationException>(action);
        ex.Code.ShouldBe(code);
    }

    [Test]
    public async Task PersonnelPosition_EffectiveDating_FutureAssignment_NotEffective()
    {
        var futureDate = Now.AddDays(10);

        var assignment = _personnel.AssignPosition(_positionId, false, futureDate, null, Now, PositionCompanies());
        await Context.SaveChangesAsync(default);

        assignment.IsCurrentlyEffective(Now).ShouldBeFalse();
        assignment.IsCurrentlyEffective(futureDate.AddDays(1)).ShouldBeTrue();
    }

    [Test]
    public async Task PersonnelPosition_EffectiveDating_PastAssignment_NotEffective()
    {
        var assignment = _personnel.AssignPosition(_positionId, false, Now.AddDays(-10), Now.AddDays(-5), Now, PositionCompanies());
        await Context.SaveChangesAsync(default);

        assignment.IsCurrentlyEffective(Now).ShouldBeFalse();
    }

    [Test]
    public async Task PersonnelPosition_EffectiveDating_CurrentAssignment_IsEffective()
    {
        var assignment = _personnel.AssignPosition(_positionId, true, Now.AddDays(-5), Now.AddDays(5), Now, PositionCompanies());
        await Context.SaveChangesAsync(default);

        assignment.IsCurrentlyEffective(Now).ShouldBeTrue();
        assignment.IsPrimary.ShouldBeTrue();
    }

    [Test]
    public async Task PersonnelPosition_EachAssignmentHasItsOwnPersistedId()
    {
        var first = _personnel.AssignPosition(_positionId, false, Now.AddDays(-20), Now.AddDays(-10), Now, PositionCompanies());
        var second = _personnel.AssignPosition(_positionId, false, Now.AddDays(-5), null, Now, PositionCompanies());
        await Context.SaveChangesAsync(default);

        first.Id.ShouldNotBe(Guid.Empty);
        second.Id.ShouldNotBe(Guid.Empty);
        first.Id.ShouldNotBe(second.Id);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var stored = await db.PersonnelPositions.AsNoTracking().Where(p => p.PersonnelId == _personnelId).Select(p => p.Id).ToListAsync();
        stored.ShouldBe(new[] { first.Id, second.Id }, ignoreOrder: true);
    }

    [Test]
    public async Task PersonnelPosition_RepeatedNonOverlappingAssignment_SamePosition_Allowed()
    {
        _personnel.AssignPosition(_positionId, false, Now.AddDays(-30), Now.AddDays(-20), Now, PositionCompanies());
        _personnel.AssignPosition(_positionId, false, Now.AddDays(-10), Now.AddDays(-5), Now, PositionCompanies());
        _personnel.AssignPosition(_positionId, false, Now.AddDays(-1), null, Now, PositionCompanies());
        await Context.SaveChangesAsync(default);

        _personnel.Positions.Count(p => p.PositionId == _positionId).ShouldBe(3);
    }

    [Test]
    public async Task PersonnelPosition_OverlappingAssignment_SamePosition_Denied()
    {
        _personnel.AssignPosition(_positionId, false, Now.AddDays(-5), Now.AddDays(5), Now, PositionCompanies());
        await Context.SaveChangesAsync(default);

        ShouldViolate(() => _personnel.AssignPosition(_positionId, false, Now.AddDays(-3), Now.AddDays(7), Now, PositionCompanies()), "ASSIGNMENT_OVERLAP");
    }

    [Test]
    public async Task PersonnelPosition_FutureReassignment_WhileCurrentExists_Allowed()
    {
        _personnel.AssignPosition(_positionId, false, Now.AddDays(-5), Now.AddDays(5), Now, PositionCompanies());
        var future = _personnel.AssignPosition(_positionId, false, Now.AddDays(10), null, Now, PositionCompanies());
        await Context.SaveChangesAsync(default);

        future.IsCurrentlyEffective(Now).ShouldBeFalse();
        _personnel.Positions.Count.ShouldBe(2);
    }

    [Test]
    public async Task PersonnelPosition_InactiveAssignment_DoesNotBlockOverlap()
    {
        var removed = _personnel.AssignPosition(_positionId, false, Now.AddDays(-5), null, Now, PositionCompanies());
        _personnel.RemovePositionAssignment(removed.Id, Now);

        var replacement = _personnel.AssignPosition(_positionId, false, Now.AddDays(-4), null, Now, PositionCompanies());
        await Context.SaveChangesAsync(default);

        replacement.IsCurrentlyEffective(Now).ShouldBeTrue();
    }

    [Test]
    public void PersonnelPosition_InvalidWindow_Denied()
    {
        ShouldViolate(() => _personnel.AssignPosition(_positionId, false, Now, Now.AddDays(-1), Now, PositionCompanies()), "INVALID_EFFECTIVE_WINDOW");
    }

    [Test]
    public async Task PersonnelPosition_Update_TargetsOnlyTheGivenAssignment()
    {
        var first = _personnel.AssignPosition(_positionId, false, Now.AddDays(1), Now.AddDays(10), Now, PositionCompanies());
        var second = _personnel.AssignPosition(_positionId, false, Now.AddDays(20), Now.AddDays(30), Now, PositionCompanies());
        await Context.SaveChangesAsync(default);
        var firstFrom = first.EffectiveFrom;
        var firstTo = first.EffectiveTo;

        _personnel.UpdatePositionAssignment(second.Id, false, Now.AddDays(15), Now.AddDays(40), PersonnelPositionStatus.Active, Now, PositionCompanies());
        await Context.SaveChangesAsync(default);

        first.EffectiveFrom.ShouldBe(firstFrom);
        first.EffectiveTo.ShouldBe(firstTo);
        second.EffectiveFrom.Date.ShouldBe(Now.AddDays(15).Date);
        second.EffectiveTo!.Value.Date.ShouldBe(Now.AddDays(40).Date);
    }

    [Test]
    public void PersonnelPosition_Update_OverlapWithSibling_Denied()
    {
        _personnel.AssignPosition(_positionId, false, Now.AddDays(1), Now.AddDays(10), Now, PositionCompanies());
        var second = _personnel.AssignPosition(_positionId, false, Now.AddDays(20), Now.AddDays(30), Now, PositionCompanies());

        ShouldViolate(
            () => _personnel.UpdatePositionAssignment(second.Id, false, Now.AddDays(5), Now.AddDays(30), PersonnelPositionStatus.Active, Now, PositionCompanies()),
            "ASSIGNMENT_OVERLAP");
    }

    [Test]
    public void PersonnelPosition_Update_OwnWindowDoesNotCountAsOverlap()
    {
        var assignment = _personnel.AssignPosition(_positionId, false, Now.AddDays(1), Now.AddDays(10), Now, PositionCompanies());

        _personnel.UpdatePositionAssignment(assignment.Id, false, Now.AddDays(2), Now.AddDays(12), PersonnelPositionStatus.Active, Now, PositionCompanies());

        assignment.EffectiveTo!.Value.Date.ShouldBe(Now.AddDays(12).Date);
    }

    [Test]
    public void PersonnelPosition_Update_EffectiveFromLockedAfterStart()
    {
        var assignment = _personnel.AssignPosition(_positionId, false, Now.AddDays(-2), Now.AddDays(10), Now, PositionCompanies());

        ShouldViolate(
            () => _personnel.UpdatePositionAssignment(assignment.Id, false, Now.AddDays(-3), Now.AddDays(10), PersonnelPositionStatus.Active, Now, PositionCompanies()),
            "EFFECTIVE_FROM_LOCKED");

        _personnel.UpdatePositionAssignment(assignment.Id, false, assignment.EffectiveFrom, Now.AddDays(20), PersonnelPositionStatus.Active, Now, PositionCompanies());
        assignment.EffectiveTo!.Value.Date.ShouldBe(Now.AddDays(20).Date);
    }

    [Test]
    public void PersonnelPosition_EndedAssignment_IsSealedForUpdateAndRemove()
    {
        var ended = _personnel.AssignPosition(_positionId, false, Now.AddDays(-20), Now.AddDays(-10), Now, PositionCompanies());

        ended.IsSealed(Now).ShouldBeTrue();
        ShouldViolate(
            () => _personnel.UpdatePositionAssignment(ended.Id, false, ended.EffectiveFrom, Now.AddDays(5), PersonnelPositionStatus.Active, Now, PositionCompanies()),
            "SEALED_RECORD");
        ShouldViolate(() => _personnel.RemovePositionAssignment(ended.Id, Now), "SEALED_RECORD");
    }

    private async Task<Guid> CreatePositionAsync(Guid companyId, string code)
    {
        var position = new Position(companyId, code, code, code);
        Context.Positions.Add(position);
        await Context.SaveChangesAsync(default);
        return position.Id;
    }

    [Test]
    public async Task Primary_SameCompany_Overlap_Denied()
    {
        var position2 = await CreatePositionAsync(_companyId, "MGR2");

        _personnel.AssignPosition(_positionId, true, Now.AddDays(-5), Now.AddDays(5), Now, PositionCompanies());

        ShouldViolate(() => _personnel.AssignPosition(position2, true, Now.AddDays(-3), Now.AddDays(7), Now, PositionCompanies()), "PRIMARY_OVERLAP_CONFLICT");
        _personnel.Positions.Count(p => p.IsPrimary).ShouldBe(1);
    }

    [Test]
    public async Task Primary_DifferentCompanies_Overlap_Allowed()
    {
        var otherCompany = await CreateCompanyAsync("OTHERCO", "Other Company");
        var otherPosition = await CreatePositionAsync(otherCompany, "OTHERPOS");

        _personnel.AssignPosition(_positionId, true, Now.AddDays(-5), Now.AddDays(5), Now, PositionCompanies());
        _personnel.AssignPosition(otherPosition, true, Now.AddDays(-3), Now.AddDays(7), Now, PositionCompanies());
        await Context.SaveChangesAsync(default);

        _personnel.Positions.Count(p => p.IsPrimary && p.IsCurrentlyEffective(Now)).ShouldBe(2);
    }

    [Test]
    public async Task Primary_SameCompany_NonOverlap_Allowed()
    {
        var position2 = await CreatePositionAsync(_companyId, "MGR2");

        _personnel.AssignPosition(_positionId, true, Now.AddDays(-20), Now.AddDays(-10), Now, PositionCompanies());
        _personnel.AssignPosition(position2, true, Now.AddDays(5), Now.AddDays(15), Now, PositionCompanies());
        await Context.SaveChangesAsync(default);

        _personnel.Positions.Count(p => p.IsPrimary).ShouldBe(2);
    }

    [Test]
    public async Task Primary_SameCompany_PromotingOverlappingAssignment_Denied()
    {
        var position2 = await CreatePositionAsync(_companyId, "MGR2");

        _personnel.AssignPosition(_positionId, true, Now.AddDays(1), Now.AddDays(10), Now, PositionCompanies());
        var secondary = _personnel.AssignPosition(position2, false, Now.AddDays(5), Now.AddDays(15), Now, PositionCompanies());

        ShouldViolate(
            () => _personnel.UpdatePositionAssignment(secondary.Id, true, secondary.EffectiveFrom, secondary.EffectiveTo, PersonnelPositionStatus.Active, Now, PositionCompanies()),
            "PRIMARY_OVERLAP_CONFLICT");
        secondary.IsPrimary.ShouldBeFalse();
    }

    [Test]
    public void Primary_UnknownPositionCompany_FailsClosed()
    {
        Should.Throw<InvalidOperationException>(() =>
            _personnel.AssignPosition(_positionId, true, Now, null, Now, new Dictionary<Guid, Guid>()));
    }
    [Test]
    public async Task Personnel_ConfirmEmployment_RequiresEffectivePosition()
    {
        var personnel = new Personnel("9876543210", "Jane", "Smith", Gender.Female);
        Context.Personnel.Add(personnel);
        await Context.SaveChangesAsync(default);

        var ex = Should.Throw<DomainRuleViolationException>(() => personnel.ConfirmEmployment(Now));
        ex.Code.ShouldBe("PERSONNEL_STATUS_TRANSITION_INVALID");
        Assert.That(ex.Message, Does.Contain("effective position"));

        // Assigning an effective position confirms employment (Draft -> Employed)
        personnel.AssignPosition(_positionId, true, Now.AddDays(-1), Now.AddDays(10), Now, PositionCompanies());
        await Context.SaveChangesAsync(default);

        personnel.Status.ShouldBe(PersonnelStatus.Employed);
    }

    [Test]
    public async Task Personnel_RevertToDraft_RequiresNoEffectivePositions()
    {
        var personnel = new Personnel("9876543210", "Jane", "Smith", Gender.Female);
        Context.Personnel.Add(personnel);
        await Context.SaveChangesAsync(default);

        personnel.AssignPosition(_positionId, true, Now.AddDays(-1), Now.AddDays(10), Now, PositionCompanies());
        await Context.SaveChangesAsync(default);
        personnel.Status.ShouldBe(PersonnelStatus.Employed);

        var ex = Should.Throw<DomainRuleViolationException>(() => personnel.RevertToDraft(Now));
        ex.Code.ShouldBe("PERSONNEL_STATUS_TRANSITION_INVALID");
        Assert.That(ex.Message, Does.Contain("effective position"));
    }

    [Test]
    public async Task Personnel_StatusTransition_DraftToEmployedAndBack()
    {
        var personnel = new Personnel("9876543210", "Jane", "Smith", Gender.Female);
        Context.Personnel.Add(personnel);
        await Context.SaveChangesAsync(default);

        var saved = await Context.Personnel.FindAsync(personnel.Id);
        saved!.Status.ShouldBe(PersonnelStatus.Draft);

        var assignment = personnel.AssignPosition(_positionId, true, Now.AddDays(-1), Now.AddDays(10), Now, PositionCompanies());
        await Context.SaveChangesAsync(default);
        personnel.Status.ShouldBe(PersonnelStatus.Employed);

        // Removing the last effective assignment returns the personnel to Draft
        personnel.RemovePositionAssignment(assignment.Id, Now);
        await Context.SaveChangesAsync(default);
        personnel.Status.ShouldBe(PersonnelStatus.Draft);
    }

    [Test]
    public async Task Position_CannotDeactivateWithActiveAssignments()
    {
        _personnel.AssignPosition(_positionId, true, Now.AddDays(-1), Now.AddDays(10), Now, PositionCompanies());
        await Context.SaveChangesAsync(default);

        var position = await Context.Positions.FindAsync(_positionId);
        Assert.That(position, Is.Not.Null);

        ShouldViolate(() => position!.SetStatus(PositionStatus.Inactive, Now), "POSITION_HAS_ACTIVE_ASSIGNMENTS");
        position!.Status.ShouldBe(PositionStatus.Active);
    }

    [Test]
    public async Task Position_CanDeactivateAfterRemovingAssignments()
    {
        var assignment = _personnel.AssignPosition(_positionId, true, Now.AddDays(-1), Now.AddDays(10), Now, PositionCompanies());
        await Context.SaveChangesAsync(default);

        var position = await Context.Positions.FindAsync(_positionId);
        Assert.That(position, Is.Not.Null);

        _personnel.RemovePositionAssignment(assignment.Id, Now);
        await Context.SaveChangesAsync(default);

        position!.SetStatus(PositionStatus.Inactive, Now);
        position.Status.ShouldBe(PositionStatus.Inactive);
    }

    [TestCase(PersonnelStatus.Draft)]
    [TestCase(PersonnelStatus.Inactive)]
    public void Personnel_ChangeStatus_ToEmployed_Denied(PersonnelStatus from)
    {
        var personnel = new Personnel("1112223334", "Jane", "Smith", Gender.Female);
        personnel.ChangeStatus(from, Now);

        ShouldViolate(() => personnel.ChangeStatus(PersonnelStatus.Employed, Now), "PERSONNEL_STATUS_TRANSITION_INVALID");
        personnel.Status.ShouldBe(from);
    }

    [Test]
    public void Personnel_Deactivate_WithEffectivePosition_Denied()
    {
        _personnel.AssignPosition(_positionId, true, Now.AddDays(-1), null, Now, PositionCompanies());

        ShouldViolate(() => _personnel.ChangeStatus(PersonnelStatus.Inactive, Now), "PERSONNEL_STATUS_TRANSITION_INVALID");
        ShouldViolate(() => _personnel.Deactivate(Now), "PERSONNEL_STATUS_TRANSITION_INVALID");
        _personnel.Status.ShouldBe(PersonnelStatus.Employed);
    }

    [Test]
    public void Personnel_DeactivateAndReactivate_WithoutPosition()
    {
        _personnel.ChangeStatus(PersonnelStatus.Inactive, Now);
        _personnel.Status.ShouldBe(PersonnelStatus.Inactive);

        _personnel.ChangeStatus(PersonnelStatus.Draft, Now);
        _personnel.Status.ShouldBe(PersonnelStatus.Draft);
    }

    [Test]
    public void Personnel_Inactive_CannotBeAssigned()
    {
        _personnel.Deactivate(Now);

        ShouldViolate(() => _personnel.AssignPosition(_positionId, true, Now, null, Now, PositionCompanies()), "PERSONNEL_INACTIVE");
        _personnel.Positions.ShouldBeEmpty();
    }

    [Test]
    public async Task Position_UpcomingAssignment_BlocksDeactivation()
    {
        _personnel.AssignPosition(_positionId, false, Now.AddDays(5), null, Now, PositionCompanies());
        await Context.SaveChangesAsync(default);
        var position = await Context.Positions.Include(p => p.Assignments).SingleAsync(p => p.Id == _positionId);

        ShouldViolate(() => position.SetStatus(PositionStatus.Inactive, Now), "POSITION_HAS_ACTIVE_ASSIGNMENTS");
        position.Status.ShouldBe(PositionStatus.Active);
    }

    [Test]
    public async Task Position_EndedAssignment_DoesNotBlockDeactivation()
    {
        _personnel.AssignPosition(_positionId, false, Now.AddDays(-20), Now.AddDays(-10), Now, PositionCompanies());
        await Context.SaveChangesAsync(default);
        var position = await Context.Positions.Include(p => p.Assignments).SingleAsync(p => p.Id == _positionId);

        position.SetStatus(PositionStatus.Inactive, Now);

        position.Status.ShouldBe(PositionStatus.Inactive);
    }
}
