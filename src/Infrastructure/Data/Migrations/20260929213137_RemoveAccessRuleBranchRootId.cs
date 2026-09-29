using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyAccessManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAccessRuleBranchRootId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccessRules_BranchRootId",
                schema: "auth",
                table: "AccessRules");

            migrationBuilder.DropColumn(
                name: "BranchRootId",
                schema: "auth",
                table: "AccessRules");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BranchRootId",
                schema: "auth",
                table: "AccessRules",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessRules_BranchRootId",
                schema: "auth",
                table: "AccessRules",
                column: "BranchRootId");
        }
    }
}
