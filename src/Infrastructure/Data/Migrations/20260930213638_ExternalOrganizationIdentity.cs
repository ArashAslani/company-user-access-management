using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyAccessManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExternalOrganizationIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                schema: "org",
                table: "Positions",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSource",
                schema: "org",
                table: "Positions",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                schema: "org",
                table: "PersonnelPositions",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSource",
                schema: "org",
                table: "PersonnelPositions",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                schema: "org",
                table: "Personnel",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSource",
                schema: "org",
                table: "Personnel",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                schema: "org",
                table: "Companies",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSource",
                schema: "org",
                table: "Companies",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Positions_CompanyId_ExternalSource_ExternalId",
                schema: "org",
                table: "Positions",
                columns: new[] { "CompanyId", "ExternalSource", "ExternalId" },
                unique: true,
                filter: "\"ExternalSource\" IS NOT NULL AND \"ExternalId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Positions_ExternalIdentity",
                schema: "org",
                table: "Positions",
                sql: "(\"ExternalSource\" IS NULL AND \"ExternalId\" IS NULL) OR (\"ExternalSource\" IS NOT NULL AND \"ExternalId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_PersonnelPositions_PersonnelId_ExternalSource_ExternalId",
                schema: "org",
                table: "PersonnelPositions",
                columns: new[] { "PersonnelId", "ExternalSource", "ExternalId" },
                unique: true,
                filter: "\"ExternalSource\" IS NOT NULL AND \"ExternalId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PersonnelPositions_ExternalIdentity",
                schema: "org",
                table: "PersonnelPositions",
                sql: "(\"ExternalSource\" IS NULL AND \"ExternalId\" IS NULL) OR (\"ExternalSource\" IS NOT NULL AND \"ExternalId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_Personnel_CompanyId_ExternalSource_ExternalId",
                schema: "org",
                table: "Personnel",
                columns: new[] { "CompanyId", "ExternalSource", "ExternalId" },
                unique: true,
                filter: "\"ExternalSource\" IS NOT NULL AND \"ExternalId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Personnel_ExternalIdentity",
                schema: "org",
                table: "Personnel",
                sql: "(\"ExternalSource\" IS NULL AND \"ExternalId\" IS NULL) OR (\"ExternalSource\" IS NOT NULL AND \"ExternalId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_ExternalSource_ExternalId",
                schema: "org",
                table: "Companies",
                columns: new[] { "ExternalSource", "ExternalId" },
                unique: true,
                filter: "\"ExternalSource\" IS NOT NULL AND \"ExternalId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Companies_ExternalIdentity",
                schema: "org",
                table: "Companies",
                sql: "(\"ExternalSource\" IS NULL AND \"ExternalId\" IS NULL) OR (\"ExternalSource\" IS NOT NULL AND \"ExternalId\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Positions_CompanyId_ExternalSource_ExternalId",
                schema: "org",
                table: "Positions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Positions_ExternalIdentity",
                schema: "org",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "IX_PersonnelPositions_PersonnelId_ExternalSource_ExternalId",
                schema: "org",
                table: "PersonnelPositions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PersonnelPositions_ExternalIdentity",
                schema: "org",
                table: "PersonnelPositions");

            migrationBuilder.DropIndex(
                name: "IX_Personnel_CompanyId_ExternalSource_ExternalId",
                schema: "org",
                table: "Personnel");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Personnel_ExternalIdentity",
                schema: "org",
                table: "Personnel");

            migrationBuilder.DropIndex(
                name: "IX_Companies_ExternalSource_ExternalId",
                schema: "org",
                table: "Companies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Companies_ExternalIdentity",
                schema: "org",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                schema: "org",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "ExternalSource",
                schema: "org",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                schema: "org",
                table: "PersonnelPositions");

            migrationBuilder.DropColumn(
                name: "ExternalSource",
                schema: "org",
                table: "PersonnelPositions");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                schema: "org",
                table: "Personnel");

            migrationBuilder.DropColumn(
                name: "ExternalSource",
                schema: "org",
                table: "Personnel");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                schema: "org",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "ExternalSource",
                schema: "org",
                table: "Companies");
        }
    }
}
