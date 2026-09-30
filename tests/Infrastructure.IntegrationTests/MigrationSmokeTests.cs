using CompanyAccessManagement.Infrastructure.Data;
using CompanyAccessManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyAccessManagement.IntegrationTests;

/// <summary>
/// Builds an empty file-backed SQLite database purely from migrations and seeds it twice.
/// </summary>
[TestFixture]
public class MigrationSmokeTests
{
    private string _databasePath = null!;
    private ServiceProvider _services = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"company-access-smoke-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true,
            Pooling = false,
        }.ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddScoped<ApplicationDbContextInitialiser>();
        _services = services.BuildServiceProvider();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm", _databasePath + "-journal" })
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Test]
    public async Task EmptyDatabase_MigratesWithoutPendingModelChanges()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>().InitialiseAsync();

        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        context.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Test]
    public async Task SeedTwice_ProducesSingleCatalogueWithoutDuplicates()
    {
        using (var scope = _services.CreateScope())
        {
            var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();
            await initialiser.InitialiseAsync();
            await initialiser.SeedAsync();
        }

        var firstRun = await SnapshotAsync();

        using (var scope = _services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>().SeedAsync();
        }

        var secondRun = await SnapshotAsync();

        firstRun.Applications.ShouldBe(1);
        firstRun.Resources.ShouldBeGreaterThan(0);
        firstRun.Permissions.ShouldBeGreaterThan(0);
        firstRun.Implications.ShouldBeGreaterThan(0);
        firstRun.Administrators.ShouldBe(1);
        secondRun.ShouldBe(firstRun);

        using var check = _services.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        (await context.Resources.GroupBy(r => new { r.ApplicationId, r.Code }).AnyAsync(g => g.Count() > 1)).ShouldBeFalse();
        (await context.Permissions.GroupBy(p => new { p.ResourceId, p.ActionCode }).AnyAsync(g => g.Count() > 1)).ShouldBeFalse();
        (await context.PermissionImplications.GroupBy(i => new { i.PermissionId, i.RequiredPermissionId }).AnyAsync(g => g.Count() > 1)).ShouldBeFalse();
    }

    private async Task<CatalogueSnapshot> SnapshotAsync()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return new CatalogueSnapshot(
            await context.Applications.CountAsync(a => a.Code == "QC"),
            await context.Resources.CountAsync(),
            await context.Permissions.CountAsync(),
            await context.PermissionImplications.CountAsync(),
            await context.Users.CountAsync(u => u.UserName == "administrator@localhost"));
    }

    private record CatalogueSnapshot(int Applications, int Resources, int Permissions, int Implications, int Administrators);
}
