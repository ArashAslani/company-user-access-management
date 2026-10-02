using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyAccessManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AttachmentsAuditAndAssignmentCorrection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CorrectsAssignmentId",
                schema: "org",
                table: "PersonnelPositions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OccurredAt",
                schema: "auth",
                table: "AuditLogs",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.Sql("""
                UPDATE "AuditLogs"
                SET "OccurredAt" = CASE
                    WHEN "Created" LIKE '%+00:00' THEN substr("Created", 1, length("Created") - 6)
                    ELSE datetime("Created")
                END;
                """);

            migrationBuilder.CreateTable(
                name: "Attachments",
                schema: "org",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerType = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Content = table.Column<byte[]>(type: "BLOB", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attachments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "org",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActorUserId_OccurredAt",
                schema: "auth",
                table: "AuditLogs",
                columns: new[] { "ActorUserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CompanyId_OccurredAt",
                schema: "auth",
                table: "AuditLogs",
                columns: new[] { "CompanyId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_CompanyId",
                schema: "org",
                table: "Attachments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_OwnerType_OwnerId",
                schema: "org",
                table: "Attachments",
                columns: new[] { "OwnerType", "OwnerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attachments",
                schema: "org");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_ActorUserId_OccurredAt",
                schema: "auth",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_CompanyId_OccurredAt",
                schema: "auth",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "CorrectsAssignmentId",
                schema: "org",
                table: "PersonnelPositions");

            migrationBuilder.DropColumn(
                name: "OccurredAt",
                schema: "auth",
                table: "AuditLogs");
        }
    }
}
