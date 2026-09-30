using CompanyAccessManagement.Application.Common.Interfaces;
using CompanyAccessManagement.Domain.Common;
using CompanyAccessManagement.Domain.Organization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyAccessManagement.IntegrationTests;

/// <summary>
/// ERP/HR identities: domain normalization, the database backstop (filtered unique indexes and the both-or-neither
/// check, bypassing handler pre-checks) and the resolver.
/// </summary>
[TestFixture]
public class ExternalIdentityTests : TestBase
{
    private Guid _companyA;
    private Guid _companyB;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        _companyA = await CreateCompanyAsync("CA", "Company A");
        _companyB = await CreateCompanyAsync("CB", "Company B");
    }

    [Test]
    public async Task ExternalIdentity_SameSourceSameId_DuplicateRejected()
    {
        await SaveAsync(db => db.Companies.Add(WithIdentity(new Company("E1", "First"), "SAP", "1000")));

        var ex = await Should.ThrowAsync<DomainRuleViolationException>(() =>
            SaveAsync(db => db.Companies.Add(WithIdentity(new Company("E2", "Second"), " sap ", "1000"))));

        ex.Code.ShouldBe("EXTERNAL_IDENTITY_DUPLICATE");
        (await CountAsync(db => db.Companies.CountAsync(c => c.ExternalId == "1000"))).ShouldBe(1);
    }

    [Test]
    public async Task ExternalIdentity_SameIdDifferentSource_Allowed()
    {
        await SaveAsync(db => db.Companies.Add(WithIdentity(new Company("E1", "First"), "SAP", "1000")));
        await SaveAsync(db => db.Companies.Add(WithIdentity(new Company("E2", "Second"), "HRIS", "1000")));

        (await CountAsync(db => db.Companies.CountAsync(c => c.ExternalId == "1000"))).ShouldBe(2);
    }

    [Test]
    public void ExternalIdentity_Incomplete_RejectedByDomain()
    {
        var company = new Company("E1", "First");

        Should.Throw<DomainRuleViolationException>(() => company.SetExternalIdentity("SAP", null)).Code.ShouldBe("EXTERNAL_IDENTITY_INCOMPLETE");
        Should.Throw<DomainRuleViolationException>(() => company.SetExternalIdentity(null, "1000")).Code.ShouldBe("EXTERNAL_IDENTITY_INCOMPLETE");
        Should.Throw<DomainRuleViolationException>(() => company.SetExternalIdentity("  ", "1000")).Code.ShouldBe("EXTERNAL_IDENTITY_INCOMPLETE");
        Should.Throw<DomainRuleViolationException>(() => company.SetExternalIdentity("SAP", "   ")).Code.ShouldBe("EXTERNAL_IDENTITY_INCOMPLETE");

        company.SetExternalIdentity("  sap ", " 00-1 ");
        company.ExternalSource.ShouldBe("SAP");
        company.ExternalId.ShouldBe(" 00-1 ");
    }

    [Test]
    public async Task ExternalIdentity_HalfSetRow_RejectedByCheckConstraint()
    {
        await SaveAsync(db => db.Positions.Add(new Position(_companyA, "P1", "Title")));

        var ex = await Should.ThrowAsync<SqliteException>(() => WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE \"Positions\" SET \"ExternalSource\" = 'SAP' WHERE \"Code\" = 'P1'")));

        ex.Message.ShouldContain("CK_Positions_ExternalIdentity");
    }

    [Test]
    public async Task PositionExternalIdentity_SameCompanyDuplicateRejected()
    {
        await SaveAsync(db => db.Positions.Add(WithIdentity(new Position(_companyA, "P1", "One"), "SAP", "POS-1")));

        var ex = await Should.ThrowAsync<DomainRuleViolationException>(() =>
            SaveAsync(db => db.Positions.Add(WithIdentity(new Position(_companyA, "P2", "Two"), "SAP", "POS-1"))));

        ex.Code.ShouldBe("EXTERNAL_IDENTITY_DUPLICATE");
        (await CountAsync(db => db.Positions.CountAsync(p => p.ExternalId == "POS-1"))).ShouldBe(1);
    }

    [Test]
    public async Task PositionExternalIdentity_DifferentCompanyAllowed()
    {
        await SaveAsync(db => db.Positions.Add(WithIdentity(new Position(_companyA, "P1", "One"), "SAP", "POS-1")));
        await SaveAsync(db => db.Positions.Add(WithIdentity(new Position(_companyB, "P1", "One"), "SAP", "POS-1")));

        (await CountAsync(db => db.Positions.CountAsync(p => p.ExternalId == "POS-1"))).ShouldBe(2);
    }

    [Test]
    public async Task PersonnelExternalIdentity_SameCompanyDuplicateRejected()
    {
        await SaveAsync(db => db.Personnel.Add(WithIdentity(NewPersonnel(_companyA, "1111111111"), "HRIS", "EMP-1")));

        var ex = await Should.ThrowAsync<DomainRuleViolationException>(() =>
            SaveAsync(db => db.Personnel.Add(WithIdentity(NewPersonnel(_companyA, "2222222222"), "HRIS", "EMP-1"))));

        ex.Code.ShouldBe("EXTERNAL_IDENTITY_DUPLICATE");
        (await CountAsync(db => db.Personnel.CountAsync(p => p.ExternalId == "EMP-1"))).ShouldBe(1);
    }

    [Test]
    public async Task PersonnelExternalIdentity_DifferentCompanyAllowed()
    {
        await SaveAsync(db => db.Personnel.Add(WithIdentity(NewPersonnel(_companyA, "1111111111"), "HRIS", "EMP-1")));
        await SaveAsync(db => db.Personnel.Add(WithIdentity(NewPersonnel(_companyB, "1111111111"), "HRIS", "EMP-1")));

        (await CountAsync(db => db.Personnel.CountAsync(p => p.ExternalId == "EMP-1"))).ShouldBe(2);
    }

    [Test]
    public async Task PersonnelPositionExternalIdentity_SamePersonnelDuplicateRejected()
    {
        var (personnelId, first, second) = await SeedPersonnelWithTwoPositionsAsync();
        await AssignAsync(personnelId, first, "ASG-1");

        var ex = await Should.ThrowAsync<DomainRuleViolationException>(() => AssignAsync(personnelId, second, "ASG-1"));

        ex.Code.ShouldBe("EXTERNAL_IDENTITY_DUPLICATE");
        (await CountAsync(db => db.PersonnelPositions.CountAsync(pp => pp.ExternalId == "ASG-1"))).ShouldBe(1);
    }

    [Test]
    public async Task ExternalLookup_ReturnsCorrectEntity()
    {
        var company = WithIdentity(new Company("E1", "External"), "SAP", "C-1");
        var positionA = WithIdentity(new Position(_companyA, "P1", "One"), "SAP", "POS-1");
        var positionB = WithIdentity(new Position(_companyB, "P1", "One"), "SAP", "POS-1");
        var personnel = WithIdentity(NewPersonnel(_companyA, "1111111111"), "HRIS", "EMP-1");
        await SaveAsync(db =>
        {
            db.Companies.Add(company);
            db.Positions.AddRange(positionA, positionB);
            db.Personnel.Add(personnel);
        });
        var assignmentId = await AssignAsync(personnel.Id, positionA.Id, "ASG-1");

        await WithResolverAsync(async resolver =>
        {
            (await resolver.ResolveCompanyAsync("sap", "C-1")).ShouldBe(company.Id);
            (await resolver.ResolvePositionAsync(_companyA, " Sap ", "POS-1")).ShouldBe(positionA.Id);
            (await resolver.ResolvePositionAsync(_companyB, "SAP", "POS-1")).ShouldBe(positionB.Id);
            (await resolver.ResolvePersonnelAsync(_companyA, "hris", "EMP-1")).ShouldBe(personnel.Id);
            (await resolver.ResolvePersonnelPositionAsync(personnel.Id, "HRIS", "ASG-1")).ShouldBe(assignmentId);
        });
    }

    [Test]
    public async Task ExternalLookup_Unknown_ReturnsNotFound()
    {
        var personnel = WithIdentity(NewPersonnel(_companyA, "1111111111"), "HRIS", "EMP-1");
        await SaveAsync(db => db.Personnel.Add(personnel));

        await WithResolverAsync(async resolver =>
        {
            (await resolver.ResolveCompanyAsync("SAP", "missing")).ShouldBeNull();
            (await resolver.ResolvePositionAsync(_companyA, "SAP", "missing")).ShouldBeNull();
            (await resolver.ResolvePersonnelAsync(_companyA, "HRIS", "emp-1")).ShouldBeNull();
            (await resolver.ResolvePersonnelAsync(_companyB, "HRIS", "EMP-1")).ShouldBeNull();
            (await resolver.ResolvePersonnelAsync(_companyA, "SAP", "EMP-1")).ShouldBeNull();
            (await resolver.ResolvePersonnelPositionAsync(personnel.Id, "HRIS", "missing")).ShouldBeNull();
        });
    }

    private static T WithIdentity<T>(T entity, string source, string id) where T : class
    {
        switch (entity)
        {
            case Company c: c.SetExternalIdentity(source, id); break;
            case Position p: p.SetExternalIdentity(source, id); break;
            case Personnel p: p.SetExternalIdentity(source, id); break;
            default: throw new ArgumentOutOfRangeException(nameof(entity));
        }
        return entity;
    }

    private static Personnel NewPersonnel(Guid companyId, string nationalCode)
        => new(companyId, nationalCode, "Test", "Person", Gender.Male);

    private async Task<(Guid PersonnelId, Guid First, Guid Second)> SeedPersonnelWithTwoPositionsAsync()
    {
        var personnel = NewPersonnel(_companyA, "1111111111");
        var first = new Position(_companyA, "P1", "One");
        var second = new Position(_companyA, "P2", "Two");
        await SaveAsync(db =>
        {
            db.Personnel.Add(personnel);
            db.Positions.AddRange(first, second);
        });
        return (personnel.Id, first.Id, second.Id);
    }

    private async Task<Guid> AssignAsync(Guid personnelId, Guid positionId, string externalId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var personnel = await db.Personnel.Include(p => p.Positions).SingleAsync(p => p.Id == personnelId);
        var companies = await db.Positions.Where(p => p.CompanyId == personnel.CompanyId).ToDictionaryAsync(p => p.Id, p => p.CompanyId);
        var assignment = personnel.AssignPosition(positionId, false, DateTime.UtcNow.AddDays(-1), null, DateTime.UtcNow, companies);
        assignment.SetExternalIdentity("HRIS", externalId);
        await db.SaveChangesAsync(default);
        return assignment.Id;
    }

    /// <summary>Each save runs in its own scope so a rejected insert never lingers in a shared change tracker.</summary>
    private async Task SaveAsync(Action<IApplicationDbContext> arrange)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        arrange(db);
        await db.SaveChangesAsync(default);
    }

    private async Task<T> CountAsync<T>(Func<IApplicationDbContext, Task<T>> query)
    {
        using var scope = Factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<IApplicationDbContext>());
    }

    private async Task WithDbAsync(Func<Infrastructure.Data.ApplicationDbContext, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<Infrastructure.Data.ApplicationDbContext>());
    }

    private async Task WithResolverAsync(Func<IExternalOrganizationResolver, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<IExternalOrganizationResolver>());
    }
}
