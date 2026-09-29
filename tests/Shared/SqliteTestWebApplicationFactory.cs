using CompanyAccessManagement.Infrastructure.Data;
using CompanyAccessManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyAccessManagement.Testing;

/// <summary>
/// Hosts the real <c>Program</c> pipeline in the TestIntegration environment against a private,
/// file-backed SQLite database that is migrated and seeded by the application itself on startup.
/// </summary>
public class SqliteTestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public SqliteTestWebApplicationFactory()
    {
        DatabasePath = Path.Combine(Path.GetTempPath(), $"company-access-api-{Guid.NewGuid():N}.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true,
            Pooling = false,
        }.ToString();
    }

    public string DatabasePath { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("TestIntegration");
        builder.UseSetting("ConnectionStrings:" + CompanyAccessManagement.Shared.Services.Database, _connectionString);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IEmailSender<ApplicationUser>, NoOpEmailSender>();
        });
    }

    /// <summary>
    /// Starts the host (which migrates and seeds) and verifies the application is really using this factory's database.
    /// </summary>
    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var connectionString = db.Database.GetConnectionString();
        if (connectionString is null || !connectionString.Contains(DatabasePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Test host is not using the test database. Connection string: '{connectionString}'.");

        if ((await db.Database.GetPendingMigrationsAsync()).Any())
            throw new InvalidOperationException("Test database has pending migrations after startup.");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { DatabasePath, DatabasePath + "-wal", DatabasePath + "-shm", DatabasePath + "-journal" })
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
                // A leftover temp file must not fail the test run.
            }
        }
    }

    private sealed class NoOpEmailSender : IEmailSender<ApplicationUser>
    {
        public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) => Task.CompletedTask;
        public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) => Task.CompletedTask;
        public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) => Task.CompletedTask;
    }
}
