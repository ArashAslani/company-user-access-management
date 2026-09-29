using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyAccessManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRolePrincipalId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every role gets exactly one AuthPrincipal (Type 1 = Role) before the legacy column is dropped.
            migrationBuilder.Sql(
                """
                INSERT INTO "AuthPrincipals" ("Id", "Type", "ReferenceId", "CompanyId", "ApplicationId", "Created", "LastModified")
                SELECT upper(hex(randomblob(4))) || '-' || upper(hex(randomblob(2))) || '-4'
                    || substr(upper(hex(randomblob(2))), 2) || '-'
                    || substr('89AB', 1 + (abs(random()) % 4), 1) || substr(upper(hex(randomblob(2))), 2) || '-'
                    || upper(hex(randomblob(6))),
                    1, r."Id", r."CompanyId", r."ApplicationId", r."Created", r."LastModified"
                FROM "Roles" r
                WHERE NOT EXISTS (
                    SELECT 1 FROM "AuthPrincipals" ap
                    WHERE ap."Type" = 1 AND ap."ReferenceId" = r."Id"
                      AND ap."CompanyId" = r."CompanyId" AND ap."ApplicationId" = r."ApplicationId");
                """);

            migrationBuilder.DropIndex(
                name: "IX_Roles_PrincipalId",
                schema: "auth",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "PrincipalId",
                schema: "auth",
                table: "Roles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PrincipalId",
                schema: "auth",
                table: "Roles",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql(
                """
                UPDATE "Roles"
                SET "PrincipalId" = COALESCE(
                    (SELECT ap."Id" FROM "AuthPrincipals" ap WHERE ap."Type" = 1 AND ap."ReferenceId" = "Roles"."Id" LIMIT 1),
                    upper(hex(randomblob(4))) || '-' || upper(hex(randomblob(2))) || '-4'
                    || substr(upper(hex(randomblob(2))), 2) || '-'
                    || substr('89AB', 1 + (abs(random()) % 4), 1) || substr(upper(hex(randomblob(2))), 2) || '-'
                    || upper(hex(randomblob(6))));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_PrincipalId",
                schema: "auth",
                table: "Roles",
                column: "PrincipalId",
                unique: true);
        }
    }
}
