using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyAccessManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchRootIdToAccessRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccessRules_AuthPrincipals_PrincipalId1",
                schema: "auth",
                table: "AccessRules");

            migrationBuilder.DropForeignKey(
                name: "FK_AccessRules_Permissions_PermissionId1",
                schema: "auth",
                table: "AccessRules");

            migrationBuilder.DropForeignKey(
                name: "FK_AccessRules_Roles_AuthorityRoleId1",
                schema: "auth",
                table: "AccessRules");

            migrationBuilder.DropIndex(
                name: "IX_AccessRules_AuthorityRoleId1",
                schema: "auth",
                table: "AccessRules");

            migrationBuilder.DropIndex(
                name: "IX_AccessRules_PermissionId1",
                schema: "auth",
                table: "AccessRules");

            migrationBuilder.DropColumn(
                name: "AuthorityRoleId1",
                schema: "auth",
                table: "AccessRules");

            migrationBuilder.DropColumn(
                name: "PermissionId1",
                schema: "auth",
                table: "AccessRules");

            migrationBuilder.RenameColumn(
                name: "PrincipalId1",
                schema: "auth",
                table: "AccessRules",
                newName: "BranchRootId");

            migrationBuilder.RenameIndex(
                name: "IX_AccessRules_PrincipalId1",
                schema: "auth",
                table: "AccessRules",
                newName: "IX_AccessRules_BranchRootId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "BranchRootId",
                schema: "auth",
                table: "AccessRules",
                newName: "PrincipalId1");

            migrationBuilder.RenameIndex(
                name: "IX_AccessRules_BranchRootId",
                schema: "auth",
                table: "AccessRules",
                newName: "IX_AccessRules_PrincipalId1");

            migrationBuilder.AddColumn<Guid>(
                name: "AuthorityRoleId1",
                schema: "auth",
                table: "AccessRules",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PermissionId1",
                schema: "auth",
                table: "AccessRules",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessRules_AuthorityRoleId1",
                schema: "auth",
                table: "AccessRules",
                column: "AuthorityRoleId1");

            migrationBuilder.CreateIndex(
                name: "IX_AccessRules_PermissionId1",
                schema: "auth",
                table: "AccessRules",
                column: "PermissionId1");

            migrationBuilder.AddForeignKey(
                name: "FK_AccessRules_AuthPrincipals_PrincipalId1",
                schema: "auth",
                table: "AccessRules",
                column: "PrincipalId1",
                principalSchema: "auth",
                principalTable: "AuthPrincipals",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AccessRules_Permissions_PermissionId1",
                schema: "auth",
                table: "AccessRules",
                column: "PermissionId1",
                principalSchema: "auth",
                principalTable: "Permissions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AccessRules_Roles_AuthorityRoleId1",
                schema: "auth",
                table: "AccessRules",
                column: "AuthorityRoleId1",
                principalSchema: "auth",
                principalTable: "Roles",
                principalColumn: "Id");
        }
    }
}
