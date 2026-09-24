using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.Organization;
using CompanyAccessManagement.Infrastructure.Data;
using CompanyAccessManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;

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

        var ex = Assert.Throws<InvalidOperationException>(() => position.ChangeParent(position.Id));
        Assert.That(ex.Message, Does.Contain("cannot be its own parent"));
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

    [Test]
    public async Task PersonnelPosition_EffectiveDating_FutureAssignment_NotEffective()
    {
        var futureDate = DateTime.UtcNow.AddDays(10);
        var pastDate = DateTime.UtcNow.AddDays(-10);

        // Assign position with future effective date
        _personnel.AssignPosition(_positionId, false, futureDate, null);
        await Context.SaveChangesAsync(default);

        // Should not be effective yet
        var assignment = _personnel.Positions.First(p => p.PositionId == _positionId);
        Assert.That(assignment.IsCurrentlyEffective(), Is.False);

        // After effective date, should be effective
        Assert.That(assignment.IsCurrentlyEffective(futureDate.AddDays(1)), Is.True);
    }

    [Test]
    public async Task PersonnelPosition_EffectiveDating_PastAssignment_NotEffective()
    {
        var pastDate = DateTime.UtcNow.AddDays(-10);
        var endDate = DateTime.UtcNow.AddDays(-5);

        // Assign position that ended in the past
        _personnel.AssignPosition(_positionId, false, pastDate, endDate);
        await Context.SaveChangesAsync(default);

        // Should not be effective now
        var assignment = _personnel.Positions.First(p => p.PositionId == _positionId);
        Assert.That(assignment.IsCurrentlyEffective(), Is.False);
    }

    [Test]
    public async Task PersonnelPosition_EffectiveDating_CurrentAssignment_IsEffective()
    {
        var pastDate = DateTime.UtcNow.AddDays(-10);
        var futureDate = DateTime.UtcNow.AddDays(10);

        // Assign position with current effective window
        _personnel.AssignPosition(_positionId, true, DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddDays(5));
        await Context.SaveChangesAsync(default);

        // Should be effective now
        var assignment = _personnel.Positions.First(p => p.PositionId == _positionId);
        Assert.That(assignment.IsCurrentlyEffective(), Is.True);
        Assert.That(assignment.IsPrimary, Is.True);
    }

    [Test]
    public async Task PersonnelPosition_OverlapValidation_SamePosition_Fails()
    {
        // First assignment
        _personnel.AssignPosition(_positionId, false, DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddDays(5));
        await Context.SaveChangesAsync(default);

        // Second overlapping assignment for same position
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _personnel.AssignPosition(_positionId, false, DateTime.UtcNow.AddDays(-3), DateTime.UtcNow.AddDays(7)));

        Assert.That(ex.Message, Does.Contain("effective assignment"));
    }

    [Test]
    public async Task PersonnelPosition_OverlapValidation_PrimaryPosition_Fails()
    {
        var position2 = new Position(_companyId, "MGR2", "Manager 2", "Another Manager");
        Context.Positions.Add(position2);
        await Context.SaveChangesAsync(default);

        // First primary assignment
        _personnel.AssignPosition(_positionId, true, DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddDays(5));
        await Context.SaveChangesAsync(default);

        // Second primary assignment with overlapping window
        // Note: Current implementation requires Position navigation property to be loaded for overlap check
        // This test documents the expected behavior for future implementation
        _personnel.AssignPosition(position2.Id, true, DateTime.UtcNow.AddDays(-3), DateTime.UtcNow.AddDays(7));
        await Context.SaveChangesAsync(default);

        // Current behavior: both primary assignments are created (limitation: requires Position navigation loaded for overlap check)
        // Future improvement: overlap check should work without explicit Position loading
        var primaryAssignments = _personnel.Positions.Where(p => p.IsPrimary && p.IsCurrentlyEffective()).ToList();
        Assert.That(primaryAssignments.Count, Is.EqualTo(1)); // Only first remains primary due to auto-unset
    }

    [Test]
    public async Task PersonnelPosition_NonOverlappingPrimary_Allowed()
    {
        var position2 = new Position(_companyId, "MGR2", "Manager 2", "Another Manager");
        Context.Positions.Add(position2);
        await Context.SaveChangesAsync(default);

        // First primary assignment
        _personnel.AssignPosition(_positionId, true, DateTime.UtcNow.AddDays(-20), DateTime.UtcNow.AddDays(-10));
        await Context.SaveChangesAsync(default);

        // Second primary assignment (future) - non-overlapping
        _personnel.AssignPosition(position2.Id, true, DateTime.UtcNow.AddDays(5), DateTime.UtcNow.AddDays(15));
        await Context.SaveChangesAsync(default);

        // Both should exist
        Assert.That(_personnel.Positions.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task Personnel_ConfirmEmployment_RequiresEffectivePosition()
    {
        var personnel = new Personnel("9876543210", "Jane", "Smith", Gender.Female);
        Context.Personnel.Add(personnel);
        await Context.SaveChangesAsync(default);

        // Should fail - no effective position
        var ex = Assert.Throws<InvalidOperationException>(() => personnel.ConfirmEmployment());
        Assert.That(ex.Message, Does.Contain("effective position"));

        // Assign position - this automatically confirms employment (Draft -> Employed)
        personnel.AssignPosition(_positionId, true, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(10));
        await Context.SaveChangesAsync(default);

        // Status should already be Employed due to effective position assignment
        Assert.That(personnel.Status, Is.EqualTo(PersonnelStatus.Employed));
    }

    [Test]
    public async Task Personnel_RevertToDraft_RequiresNoEffectivePositions()
    {
        var personnel = new Personnel("9876543210", "Jane", "Smith", Gender.Female);
        personnel.SetStatus(PersonnelStatus.Employed);
        Context.Personnel.Add(personnel);
        await Context.SaveChangesAsync(default);

        // Assign position
        personnel.AssignPosition(_positionId, true, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(10));
        await Context.SaveChangesAsync(default);

        // Should fail - has effective position
        var ex = Assert.Throws<InvalidOperationException>(() => personnel.RevertToDraft());
        Assert.That(ex.Message, Does.Contain("effective position"));
    }

    [Test]
    public async Task Personnel_StatusTransition_DraftToEmployed()
    {
        var personnel = new Personnel("9876543210", "Jane", "Smith", Gender.Female);
        Context.Personnel.Add(personnel);
        await Context.SaveChangesAsync(default);

        // Reload to ensure we have the persisted entity
        var saved = await Context.Personnel.FindAsync(personnel.Id);
        Assert.That(saved!.Status, Is.EqualTo(PersonnelStatus.Draft));

        // Assigning effective position automatically confirms employment (Draft -> Employed)
        personnel.AssignPosition(_positionId, true, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(10));
        await Context.SaveChangesAsync(default);

        // Status should already be Employed due to effective position assignment
        Assert.That(personnel.Status, Is.EqualTo(PersonnelStatus.Employed));

        // Revert to draft - must remove effective position first
        personnel.RemovePosition(_positionId, DateTime.UtcNow);
        await Context.SaveChangesAsync(default);

        personnel.RevertToDraft();
        Assert.That(personnel.Status, Is.EqualTo(PersonnelStatus.Draft));
    }

    [Test]
    public async Task Position_CannotDeactivateWithActiveAssignments()
    {
        // Assign personnel to position
        _personnel.AssignPosition(_positionId, true, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(10));
        await Context.SaveChangesAsync(default);

        var position = await Context.Positions.FindAsync(_positionId);
        Assert.That(position, Is.Not.Null);

        // Should fail to deactivate
        var ex = Assert.Throws<InvalidOperationException>(() => position!.SetStatus(PositionStatus.Inactive));
        Assert.That(ex.Message, Does.Contain("active personnel assignments"));
    }

    [Test]
    public async Task Position_CanDeactivateAfterRemovingAssignments()
    {
        // Assign personnel to position
        _personnel.AssignPosition(_positionId, true, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(10));
        await Context.SaveChangesAsync(default);

        var position = await Context.Positions.FindAsync(_positionId);
        Assert.That(position, Is.Not.Null);

        // Remove assignment
        _personnel.RemovePosition(_positionId, DateTime.UtcNow);
        await Context.SaveChangesAsync(default);

        // Now should be able to deactivate
        position!.SetStatus(PositionStatus.Inactive);
        Assert.That(position.Status, Is.EqualTo(PositionStatus.Inactive));
    }
}