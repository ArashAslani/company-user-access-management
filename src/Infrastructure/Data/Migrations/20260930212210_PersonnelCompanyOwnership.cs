using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyAccessManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersonnelCompanyOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Personnel_NationalCode",
                schema: "org",
                table: "Personnel");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                schema: "org",
                table: "Personnel",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Deterministic ownership backfill (ADR-0006):
            // 1. every assignment (any status) points to one company: use it;
            // 2. no assignment at all and exactly one root company exists: use the root company;
            // 3. anything else is ambiguous and aborts the migration for manual resolution.
            migrationBuilder.Sql("""
                UPDATE "Personnel"
                SET "CompanyId" = (
                    SELECT MIN(pos."CompanyId")
                    FROM "PersonnelPositions" pp
                    JOIN "Positions" pos ON pos."Id" = pp."PositionId"
                    WHERE pp."PersonnelId" = "Personnel"."Id")
                WHERE (
                    SELECT COUNT(DISTINCT pos."CompanyId")
                    FROM "PersonnelPositions" pp
                    JOIN "Positions" pos ON pos."Id" = pp."PositionId"
                    WHERE pp."PersonnelId" = "Personnel"."Id") = 1;
                """);

            migrationBuilder.Sql("""
                UPDATE "Personnel"
                SET "CompanyId" = (SELECT "Id" FROM "Companies" WHERE "ParentCompanyId" IS NULL)
                WHERE NOT EXISTS (SELECT 1 FROM "PersonnelPositions" pp WHERE pp."PersonnelId" = "Personnel"."Id")
                  AND (SELECT COUNT(*) FROM "Companies" WHERE "ParentCompanyId" IS NULL) = 1;
                """);

            migrationBuilder.Sql("""
                CREATE TEMP TABLE "__PersonnelCompanyResolution" (
                    "PersonnelId" TEXT NOT NULL,
                    CONSTRAINT "personnel_company_unresolved_assign_manually" CHECK (0));
                INSERT INTO "__PersonnelCompanyResolution" ("PersonnelId")
                SELECT "Id" FROM "Personnel" WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';
                DROP TABLE "__PersonnelCompanyResolution";
                """);

            migrationBuilder.CreateIndex(

                name: "IX_Personnel_CompanyId_NationalCode",
                schema: "org",
                table: "Personnel",
                columns: new[] { "CompanyId", "NationalCode" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Personnel_Companies_CompanyId",
                schema: "org",
                table: "Personnel",
                column: "CompanyId",
                principalSchema: "org",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Personnel_Companies_CompanyId",
                schema: "org",
                table: "Personnel");

            migrationBuilder.DropIndex(
                name: "IX_Personnel_CompanyId_NationalCode",
                schema: "org",
                table: "Personnel");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                schema: "org",
                table: "Personnel");

            migrationBuilder.CreateIndex(
                name: "IX_Personnel_NationalCode",
                schema: "org",
                table: "Personnel",
                column: "NationalCode",
                unique: true);
        }
    }
}
