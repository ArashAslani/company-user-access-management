using CompanyAccessManagement.Domain.AccessControl;
using CompanyAccessManagement.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CompanyAccessManagement.IntegrationTests;

/// <summary>
/// Upgrades a database created by an older migration and checks that forward migrations preserve existing data.
/// </summary>
[TestFixture]
public class MigrationUpgradeTests
{
    private const string LegacyCompositeKeyMigration = "20260925114254_AddBranchRootIdToAccessRules";
    private const string BeforeRemoveRolePrincipalIdMigration = "20260929213137_RemoveAccessRuleBranchRootId";
    private const string BeforePersonnelCompanyOwnershipMigration = "20260930180706_AccountPersonnelUniqueness";

    private string _databasePath = null!;
    private string _connectionString = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"company-access-migration-{Guid.NewGuid():N}.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true,
            Pooling = false,
        }.ToString();
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm", _databasePath + "-journal" })
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Test]
    public async Task PersonnelPositionIdentity_BackfillsUniqueIdsAndPreservesRows()
    {
        var personnelId = Guid.NewGuid().ToString().ToUpperInvariant();
        var positionA = Guid.NewGuid().ToString().ToUpperInvariant();
        var positionB = Guid.NewGuid().ToString().ToUpperInvariant();
        var companyId = Guid.NewGuid().ToString().ToUpperInvariant();

        await using (var legacy = CreateContext())
        {
            await legacy.GetService<IMigrator>().MigrateAsync(LegacyCompositeKeyMigration);

            const string stamp = "2026-01-01 00:00:00";
            var legacyRows =
                $"""
                INSERT INTO "Personnel" ("Id", "NationalCode", "FirstName", "LastName", "Gender", "Status", "Created", "LastModified")
                VALUES ('{personnelId}', '1234567890', 'Legacy', 'Row', 0, 1, '{stamp}', '{stamp}');
                INSERT INTO "Companies" ("Id", "Code", "Name", "Status", "Created", "LastModified")
                VALUES ('{companyId}', 'LEGACY', 'Legacy', 0, '{stamp}', '{stamp}');
                INSERT INTO "Positions" ("Id", "CompanyId", "Code", "Title", "Status", "Created", "LastModified")
                VALUES ('{positionA}', '{companyId}', 'A', 'Position A', 0, '{stamp}', '{stamp}'),
                       ('{positionB}', '{companyId}', 'B', 'Position B', 0, '{stamp}', '{stamp}');
                INSERT INTO "PersonnelPositions" ("PersonnelId", "PositionId", "IsPrimary", "Status", "EffectiveFrom", "CreatedAt", "Id")
                VALUES ('{personnelId}', '{positionA}', 1, 0, '{stamp}', '{stamp}', '00000000-0000-0000-0000-000000000000'),
                       ('{personnelId}', '{positionB}', 0, 0, '{stamp}', '{stamp}', '00000000-0000-0000-0000-000000000000');
                """;
            await legacy.Database.ExecuteSqlRawAsync(legacyRows);
        }

        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();

            var assignments = await upgraded.PersonnelPositions.AsNoTracking().ToListAsync();

            assignments.Count.ShouldBe(2);
            assignments.ShouldAllBe(a => a.Id != Guid.Empty);
            assignments.Select(a => a.Id).Distinct().Count().ShouldBe(2);
            assignments.Select(a => a.PositionId.ToString().ToUpperInvariant()).OrderBy(x => x)
                .ShouldBe(new[] { positionA, positionB }.OrderBy(x => x));
            assignments.Single(a => a.IsPrimary).PositionId.ShouldBe(Guid.Parse(positionA));

            (await upgraded.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        }
    }

    [Test]
    public async Task RemoveRolePrincipalId_BackfillsOneAuthPrincipalPerRole()
    {
        var appId = Guid.NewGuid().ToString().ToUpperInvariant();
        var companyId = Guid.NewGuid().ToString().ToUpperInvariant();
        var roleWithoutPrincipal = Guid.NewGuid().ToString().ToUpperInvariant();
        var roleWithPrincipal = Guid.NewGuid().ToString().ToUpperInvariant();
        var existingPrincipal = Guid.NewGuid().ToString().ToUpperInvariant();

        await using (var legacy = CreateContext())
        {
            await legacy.GetService<IMigrator>().MigrateAsync(BeforeRemoveRolePrincipalIdMigration);

            const string stamp = "2026-01-01 00:00:00";
            var legacyRows =
                $"""
                INSERT INTO "Applications" ("Id", "Code", "Name", "IsActive", "PolicyRevision", "Created", "LastModified")
                VALUES ('{appId}', 'LEGACY', 'Legacy', 1, 0, '{stamp}', '{stamp}');
                INSERT INTO "Roles" ("Id", "CompanyId", "ApplicationId", "PrincipalId", "Code", "Name", "Kind", "Status", "Created", "LastModified")
                VALUES ('{roleWithoutPrincipal}', '{companyId}', '{appId}', '{Guid.NewGuid()}', 'A', 'Role A', 0, 0, '{stamp}', '{stamp}'),
                       ('{roleWithPrincipal}', '{companyId}', '{appId}', '{Guid.NewGuid()}', 'B', 'Role B', 0, 0, '{stamp}', '{stamp}');
                INSERT INTO "AuthPrincipals" ("Id", "Type", "ReferenceId", "CompanyId", "ApplicationId", "Created", "LastModified")
                VALUES ('{existingPrincipal}', 1, '{roleWithPrincipal}', '{companyId}', '{appId}', '{stamp}', '{stamp}');
                """;
            await legacy.Database.ExecuteSqlRawAsync(legacyRows);
        }

        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();

            var principals = await upgraded.AuthPrincipals.AsNoTracking()
                .Where(ap => ap.Type == PrincipalType.Role)
                .ToListAsync();

            principals.Count(ap => ap.ReferenceId == Guid.Parse(roleWithoutPrincipal)).ShouldBe(1);
            principals.Single(ap => ap.ReferenceId == Guid.Parse(roleWithPrincipal)).Id.ShouldBe(Guid.Parse(existingPrincipal));
            principals.ShouldAllBe(ap => ap.Id != Guid.Empty && ap.CompanyId == Guid.Parse(companyId) && ap.ApplicationId == Guid.Parse(appId));
        }
    }

    [Test]
    public async Task PersonnelCompanyOwnership_SingleAssignedCompany_IsUsed()
    {
        var company = NewId();
        var otherRoot = NewId();
        var position = NewId();
        var personnel = NewId();

        await SeedBeforeOwnershipAsync(
            CompanySql(company, "ASSIGNED") + CompanySql(otherRoot, "OTHER") +
            PositionSql(position, company, "POS") +
            PersonnelSql(personnel, "1000000001") +
            AssignmentSql(personnel, position));

        await using var upgraded = CreateContext();
        await upgraded.Database.MigrateAsync();

        (await upgraded.Personnel.AsNoTracking().SingleAsync()).CompanyId.ShouldBe(Guid.Parse(company));
    }

    [Test]
    public async Task PersonnelCompanyOwnership_UnassignedWithSingleRoot_UsesRootCompany()
    {
        var root = NewId();
        var child = NewId();
        var personnel = NewId();

        await SeedBeforeOwnershipAsync(
            CompanySql(root, "ROOT") + CompanySql(child, "CHILD", parent: root) +
            PersonnelSql(personnel, "1000000002"));

        await using var upgraded = CreateContext();
        await upgraded.Database.MigrateAsync();

        (await upgraded.Personnel.AsNoTracking().SingleAsync()).CompanyId.ShouldBe(Guid.Parse(root));
    }

    [Test]
    public async Task PersonnelCompanyOwnership_AssignmentsInSeveralCompanies_FailsClearly()
    {
        var companyA = NewId();
        var companyB = NewId();
        var positionA = NewId();
        var positionB = NewId();
        var personnel = NewId();

        await SeedBeforeOwnershipAsync(
            CompanySql(companyA, "A") + CompanySql(companyB, "B") +
            PositionSql(positionA, companyA, "PA") + PositionSql(positionB, companyB, "PB") +
            PersonnelSql(personnel, "1000000003") +
            AssignmentSql(personnel, positionA) + AssignmentSql(personnel, positionB));

        await ShouldFailUnresolvedAsync();
    }

    [Test]
    public async Task PersonnelCompanyOwnership_UnassignedWithSeveralRoots_FailsClearly()
    {
        await SeedBeforeOwnershipAsync(
            CompanySql(NewId(), "ROOT1") + CompanySql(NewId(), "ROOT2") +
            PersonnelSql(NewId(), "1000000004"));

        await ShouldFailUnresolvedAsync();
    }

    [Test]
    public async Task OrganizationConcurrencyTokens_BackfillsDistinctTokensAndStartingRevisions()
    {
        var root = NewId();
        await SeedBeforeOwnershipAsync(
            CompanySql(root, "ROOT") + PersonnelSql(NewId(), "1000000005") + PersonnelSql(NewId(), "1000000006"));

        await using var upgraded = CreateContext();
        await upgraded.Database.MigrateAsync();

        var tokens = await upgraded.Personnel.AsNoTracking().Select(p => p.ConcurrencyToken).ToListAsync();
        tokens.Count.ShouldBe(2);
        tokens.ShouldAllBe(t => t != Guid.Empty);
        tokens.Distinct().Count().ShouldBe(2);

        var company = await upgraded.Companies.AsNoTracking().SingleAsync();
        company.OrganizationRevision.ShouldBe(1);
        company.AuthorizationRevision.ShouldBe(1);
    }

    private async Task ShouldFailUnresolvedAsync()
    {
        await using (var upgraded = CreateContext())
        {
            var ex = await Should.ThrowAsync<SqliteException>(() => upgraded.Database.MigrateAsync());
            ex.Message.ShouldContain("personnel_company_unresolved_assign_manually");
        }

        await using var check = CreateContext();
        (await check.Database.GetAppliedMigrationsAsync()).ShouldNotContain(m => m.EndsWith("_PersonnelCompanyOwnership"));
        (await check.Database.SqlQueryRaw<int>("""SELECT COUNT(*) AS "Value" FROM "Personnel" """).SingleAsync()).ShouldBe(1);
    }

    private async Task SeedBeforeOwnershipAsync(string sql)
    {
        await using var legacy = CreateContext();
        await legacy.GetService<IMigrator>().MigrateAsync(BeforePersonnelCompanyOwnershipMigration);
        await legacy.Database.ExecuteSqlRawAsync(sql);
    }

    private const string Stamp = "2026-01-01 00:00:00";

    private static string NewId() => Guid.NewGuid().ToString().ToUpperInvariant();

    private static string CompanySql(string id, string code, string? parent = null) =>
        $"""
        INSERT INTO "Companies" ("Id", "ParentCompanyId", "Code", "Name", "Status", "Created", "LastModified")
        VALUES ('{id}', {(parent is null ? "NULL" : $"'{parent}'")}, '{code}', '{code}', 0, '{Stamp}', '{Stamp}');
        """;

    private static string PositionSql(string id, string companyId, string code) =>
        $"""
        INSERT INTO "Positions" ("Id", "CompanyId", "Code", "Title", "Status", "Created", "LastModified")
        VALUES ('{id}', '{companyId}', '{code}', '{code}', 0, '{Stamp}', '{Stamp}');
        """;

    private static string PersonnelSql(string id, string nationalCode) =>
        $"""
        INSERT INTO "Personnel" ("Id", "NationalCode", "FirstName", "LastName", "Gender", "Status", "Created", "LastModified")
        VALUES ('{id}', '{nationalCode}', 'Legacy', 'Row', 0, 0, '{Stamp}', '{Stamp}');
        """;

    private static string AssignmentSql(string personnelId, string positionId) =>
        $"""
        INSERT INTO "PersonnelPositions" ("Id", "PersonnelId", "PositionId", "IsPrimary", "Status", "EffectiveFrom", "CreatedAt")
        VALUES ('{NewId()}', '{personnelId}', '{positionId}', 0, 0, '{Stamp}', '{Stamp}');
        """;

    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
