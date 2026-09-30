using CompanyAccessManagement.Application.Common.Exceptions;
using CompanyAccessManagement.Application.Common.Hierarchy;
using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Domain.Organization;
using CompanyAccessManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyAccessManagement.IntegrationTests;

/// <summary>
/// Two writers, each with its own DI scope (own DbContext and connection) on the same SQLite file, read the same state
/// before either commits. The optimistic tokens must reject the stale second commit; no process-local locking is involved.
/// Hierarchy writers follow the handlers: read the company revision, check for cycles, change the parent, bump the revision.
/// </summary>
[TestFixture]
public class ConcurrencyTests : TestBase
{
    private Guid _companyId;
    private Guid _appId;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        (_companyId, _appId, _, _, _) = await SeedTestDataAsync();
    }

    [Test]
    public async Task ConcurrentPrimary_SameCompany_NeverProducesTwoPrimaries()
    {
        var personnelId = await SeedPersonnelAsync(_companyId, "1111111111");
        var first = await SeedPositionAsync(_companyId, "P1");
        var second = await SeedPositionAsync(_companyId, "P2");

        using var writerA = Writer();
        using var writerB = Writer();
        await AssignPrimaryAsync(writerA.Db, personnelId, first);
        await AssignPrimaryAsync(writerB.Db, personnelId, second);

        await writerA.Db.SaveChangesAsync();
        await Should.ThrowAsync<ConcurrencyConflictException>(() => writerB.Db.SaveChangesAsync());

        var primaries = await ReadAsync(db => db.PersonnelPositions.Where(pp => pp.PersonnelId == personnelId && pp.IsPrimary).ToListAsync());
        primaries.Count.ShouldBe(1);
        primaries[0].PositionId.ShouldBe(first);
    }

    [Test]
    public async Task ConcurrentPrimary_DifferentCompanies_RemainIndependent()
    {
        var otherCompanyId = await CreateCompanyAsync("OTHER", "Other Company");
        var personnelA = await SeedPersonnelAsync(_companyId, "1111111111");
        var personnelB = await SeedPersonnelAsync(otherCompanyId, "1111111111");
        var positionA = await SeedPositionAsync(_companyId, "P1");
        var positionB = await SeedPositionAsync(otherCompanyId, "P1");

        using var writerA = Writer();
        using var writerB = Writer();
        await AssignPrimaryAsync(writerA.Db, personnelA, positionA);
        await AssignPrimaryAsync(writerB.Db, personnelB, positionB);

        await writerA.Db.SaveChangesAsync();
        await writerB.Db.SaveChangesAsync();

        (await ReadAsync(db => db.PersonnelPositions.CountAsync(pp => pp.PersonnelId == personnelA && pp.IsPrimary))).ShouldBe(1);
        (await ReadAsync(db => db.PersonnelPositions.CountAsync(pp => pp.PersonnelId == personnelB && pp.IsPrimary))).ShouldBe(1);
    }

    [Test]
    public async Task ConcurrentPersonnelEdit_StaleWriterRejected()
    {
        var personnelId = await SeedPersonnelAsync(_companyId, "1111111111");

        using var writerA = Writer();
        using var writerB = Writer();
        var staleA = await writerA.Db.Personnel.SingleAsync(p => p.Id == personnelId);
        var staleB = await writerB.Db.Personnel.SingleAsync(p => p.Id == personnelId);
        staleA.ChangeNationalCode("2222222222");
        staleB.UpdateDetails("Other", "Name", null, Gender.Female);

        await writerA.Db.SaveChangesAsync();
        await Should.ThrowAsync<ConcurrencyConflictException>(() => writerB.Db.SaveChangesAsync());

        var saved = await ReadAsync(db => db.Personnel.SingleAsync(p => p.Id == personnelId));
        saved.NationalCode.ShouldBe("2222222222");
        saved.FirstName.ShouldBe("Test");
    }

    [Test]
    public async Task ConcurrentPositionHierarchyMutation_CannotCreateCycle()
    {
        var a = await SeedPositionAsync(_companyId, "A");
        var b = await SeedPositionAsync(_companyId, "B");

        using var writerA = Writer();
        using var writerB = Writer();
        await MovePositionAsync(writerA.Db, a, newParent: b);
        await MovePositionAsync(writerB.Db, b, newParent: a);

        await writerA.Db.SaveChangesAsync();
        await Should.ThrowAsync<ConcurrencyConflictException>(() => writerB.Db.SaveChangesAsync());

        var parents = await ReadAsync(db => db.Positions.Where(p => p.Id == a || p.Id == b).ToDictionaryAsync(p => p.Id, p => p.ParentPositionId));
        parents[a].ShouldBe(b);
        parents[b].ShouldBeNull();
    }

    [Test]
    public async Task ConcurrentRoleHierarchyMutation_CannotCreateCycle()
    {
        var a = (await CreateRoleAsync(_companyId, _appId, "Role A", "ROLE_A")).Id;
        var b = (await CreateRoleAsync(_companyId, _appId, "Role B", "ROLE_B")).Id;

        using var writerA = Writer();
        using var writerB = Writer();
        await MoveRoleAsync(writerA.Db, a, newParent: b);
        await MoveRoleAsync(writerB.Db, b, newParent: a);

        await writerA.Db.SaveChangesAsync();
        await Should.ThrowAsync<ConcurrencyConflictException>(() => writerB.Db.SaveChangesAsync());

        var parents = await ReadAsync(db => db.BusinessRoles.Where(r => r.Id == a || r.Id == b).ToDictionaryAsync(r => r.Id, r => r.ParentRoleId));
        parents[a].ShouldBe(b);
        parents[b].ShouldBeNull();
    }

    private static async Task AssignPrimaryAsync(ApplicationDbContext db, Guid personnelId, Guid positionId)
    {
        var personnel = await db.Personnel.Include(p => p.Positions).SingleAsync(p => p.Id == personnelId);
        var companies = await db.Positions.Where(p => p.CompanyId == personnel.CompanyId).ToDictionaryAsync(p => p.Id, p => p.CompanyId);
        personnel.AssignPosition(positionId, isPrimary: true, DateTime.UtcNow.AddDays(-1), null, DateTime.UtcNow, companies);
    }

    private static async Task MovePositionAsync(ApplicationDbContext db, Guid positionId, Guid newParent)
    {
        var position = await db.Positions.SingleAsync(p => p.Id == positionId);
        var company = await db.Companies.SingleAsync(c => c.Id == position.CompanyId);
        company.TouchOrganization();

        await HierarchyCycle.EnsureAcyclicAsync(positionId, newParent,
            async (id, ct) => await db.Positions.Where(p => p.Id == id).Select(p => p.ParentPositionId).FirstOrDefaultAsync(ct),
            default, "Position");
        position.ChangeParent(newParent);
    }

    private static async Task MoveRoleAsync(ApplicationDbContext db, Guid roleId, Guid newParent)
    {
        var role = await db.BusinessRoles.SingleAsync(r => r.Id == roleId);
        var company = await db.Companies.SingleAsync(c => c.Id == role.CompanyId);
        company.TouchAuthorization();

        await HierarchyCycle.EnsureAcyclicAsync(roleId, newParent,
            async (id, ct) => await db.BusinessRoles.Where(r => r.Id == id).Select(r => r.ParentRoleId).FirstOrDefaultAsync(ct),
            default, "Role");
        role.ChangeParent(newParent);
    }

    private async Task<Guid> SeedPersonnelAsync(Guid companyId, string nationalCode)
    {
        var personnel = new Personnel(companyId, nationalCode, "Test", "Person", Gender.Male);
        Context.Personnel.Add(personnel);
        await Context.SaveChangesAsync(default);
        return personnel.Id;
    }

    private async Task<Guid> SeedPositionAsync(Guid companyId, string code)
    {
        var position = new Position(companyId, code, code);
        Context.Positions.Add(position);
        await Context.SaveChangesAsync(default);
        return position.Id;
    }

    private async Task<T> ReadAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        using var scope = Factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private WriterScope Writer() => new(Factory.Services.CreateScope());

    private sealed class WriterScope(IServiceScope scope) : IDisposable
    {
        public ApplicationDbContext Db { get; } = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        public void Dispose() => scope.Dispose();
    }
}
