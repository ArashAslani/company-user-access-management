using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyAccessManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccountPersonnelUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_PersonnelId",
                schema: "identity",
                table: "AspNetUsers",
                column: "PersonnelId",
                unique: true,
                filter: "\"PersonnelId\" IS NOT NULL AND \"IsDeleted\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_PersonnelId",
                schema: "identity",
                table: "AspNetUsers");
        }
    }
}
