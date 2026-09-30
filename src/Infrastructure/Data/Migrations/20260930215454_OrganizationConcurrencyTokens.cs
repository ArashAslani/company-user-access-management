using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyAccessManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                schema: "org",
                table: "Personnel",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<long>(
                name: "AuthorizationRevision",
                schema: "org",
                table: "Companies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "OrganizationRevision",
                schema: "org",
                table: "Companies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1L);

            // Existing rows get distinct random tokens in EF's upper-case GUID text format.
            migrationBuilder.Sql(
                "UPDATE \"Personnel\" SET \"ConcurrencyToken\" = " +
                "upper(hex(randomblob(4))) || '-' || upper(hex(randomblob(2))) || '-' || upper(hex(randomblob(2))) || '-' || " +
                "upper(hex(randomblob(2))) || '-' || upper(hex(randomblob(6)));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                schema: "org",
                table: "Personnel");

            migrationBuilder.DropColumn(
                name: "AuthorizationRevision",
                schema: "org",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "OrganizationRevision",
                schema: "org",
                table: "Companies");
        }
    }
}
