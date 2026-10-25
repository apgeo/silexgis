using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HistoryNamesItsSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "change_set_id",
                table: "audit_log",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "import_batch_id",
                table: "audit_log",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "source",
                table: "audit_log",
                type: "smallint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_change_set_id",
                table: "audit_log",
                column: "change_set_id",
                filter: "change_set_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_import_batch_id",
                table: "audit_log",
                column: "import_batch_id",
                filter: "import_batch_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_log_change_set_id",
                table: "audit_log");

            migrationBuilder.DropIndex(
                name: "ix_audit_log_import_batch_id",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "change_set_id",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "import_batch_id",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "source",
                table: "audit_log");
        }
    }
}
