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

        await using (var legacy = CreateContext())
        {
            await legacy.GetService<IMigrator>().MigrateAsync(LegacyCompositeKeyMigration);

            const string stamp = "2026-01-01 00:00:00";
            var legacyRows =
                $"""
                INSERT INTO "Personnel" ("Id", "NationalCode", "FirstName", "LastName", "Gender", "Status", "Created", "LastModified")
                VALUES ('{personnelId}', '1234567890', 'Legacy', 'Row', 0, 1, '{stamp}', '{stamp}');
                INSERT INTO "Positions" ("Id", "CompanyId", "Code", "Title", "Status", "Created", "LastModified")
                VALUES ('{positionA}', '{Guid.NewGuid()}', 'A', 'Position A', 0, '{stamp}', '{stamp}'),
                       ('{positionB}', '{Guid.NewGuid()}', 'B', 'Position B', 0, '{stamp}', '{stamp}');
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

    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
