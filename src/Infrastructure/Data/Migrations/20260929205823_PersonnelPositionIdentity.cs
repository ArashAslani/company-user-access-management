using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyAccessManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersonnelPositionIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rows written under the old composite key all carry Guid.Empty in "Id". Give each one a random
            // version-4 GUID in EF's upper-case text format so the column can become the primary key.
            migrationBuilder.Sql(
                """
                UPDATE "PersonnelPositions"
                SET "Id" = upper(hex(randomblob(4))) || '-' || upper(hex(randomblob(2))) || '-4'
                    || substr(upper(hex(randomblob(2))), 2) || '-'
                    || substr('89AB', 1 + (abs(random()) % 4), 1) || substr(upper(hex(randomblob(2))), 2) || '-'
                    || upper(hex(randomblob(6)))
                WHERE "Id" = '00000000-0000-0000-0000-000000000000' OR "Id" IS NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Permissions_Resources_ResourceId1",
                schema: "auth",
                table: "Permissions");

            migrationBuilder.DropForeignKey(
                name: "FK_PersonnelPositions_Positions_PositionId1",
                schema: "org",
                table: "PersonnelPositions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PersonnelPositions",
                schema: "org",
                table: "PersonnelPositions");

            migrationBuilder.DropIndex(
                name: "IX_PersonnelPositions_PositionId1",
                schema: "org",
                table: "PersonnelPositions");

            migrationBuilder.DropIndex(
                name: "IX_Permissions_ResourceId1",
                schema: "auth",
                table: "Permissions");

            migrationBuilder.DropColumn(
                name: "PositionId1",
                schema: "org",
                table: "PersonnelPositions");

            migrationBuilder.DropColumn(
                name: "ResourceId1",
                schema: "auth",
                table: "Permissions");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PersonnelPositions",
                schema: "org",
                table: "PersonnelPositions",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_PersonnelPositions_PersonnelId_PositionId_EffectiveFrom",
                schema: "org",
                table: "PersonnelPositions",
                columns: new[] { "PersonnelId", "PositionId", "EffectiveFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PersonnelPositions",
                schema: "org",
                table: "PersonnelPositions");

            migrationBuilder.DropIndex(
                name: "IX_PersonnelPositions_PersonnelId_PositionId_EffectiveFrom",
                schema: "org",
                table: "PersonnelPositions");

            migrationBuilder.AddColumn<Guid>(
                name: "PositionId1",
                schema: "org",
                table: "PersonnelPositions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResourceId1",
                schema: "auth",
                table: "Permissions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_PersonnelPositions",
                schema: "org",
                table: "PersonnelPositions",
                columns: new[] { "PersonnelId", "PositionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonnelPositions_PositionId1",
                schema: "org",
                table: "PersonnelPositions",
                column: "PositionId1");

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_ResourceId1",
                schema: "auth",
                table: "Permissions",
                column: "ResourceId1");

            migrationBuilder.AddForeignKey(
                name: "FK_Permissions_Resources_ResourceId1",
                schema: "auth",
                table: "Permissions",
                column: "ResourceId1",
                principalSchema: "auth",
                principalTable: "Resources",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PersonnelPositions_Positions_PositionId1",
                schema: "org",
                table: "PersonnelPositions",
                column: "PositionId1",
                principalSchema: "org",
                principalTable: "Positions",
                principalColumn: "Id");
        }
    }
}
