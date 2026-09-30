using System.Linq.Expressions;
using CompanyAccessManagement.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyAccessManagement.Infrastructure.Data.Configurations;

internal static class ExternalIdentityConfiguration
{
    public const string UniqueFilter = "\"ExternalSource\" IS NOT NULL AND \"ExternalId\" IS NOT NULL";

    public static string CheckConstraintName(string table) => $"CK_{table}_ExternalIdentity";

    public const string CheckConstraintSql =
        "(\"ExternalSource\" IS NULL AND \"ExternalId\" IS NULL) OR (\"ExternalSource\" IS NOT NULL AND \"ExternalId\" IS NOT NULL)";

    /// <summary>Columns, both-or-neither check and a filtered unique index over <paramref name="uniqueKey"/>.</summary>
    public static void HasExternalIdentity<T>(this EntityTypeBuilder<T> builder, Expression<Func<T, object?>> uniqueKey)
        where T : class
    {
        builder.Property<string?>("ExternalSource").HasMaxLength(ExternalIdentity.MaxSourceLength);
        builder.Property<string?>("ExternalId").HasMaxLength(ExternalIdentity.MaxIdLength);
        builder.HasIndex(uniqueKey).IsUnique().HasFilter(UniqueFilter);
    }
}
