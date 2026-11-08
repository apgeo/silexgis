using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StorageReportSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "storage_report_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    taken_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    store_used_bytes = table.Column<long>(type: "bigint", nullable: false),
                    scan_used_bytes = table.Column<long>(type: "bigint", nullable: false),
                    store_free_bytes = table.Column<long>(type: "bigint", nullable: true),
                    report = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_report_snapshots", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_storage_report_snapshots_taken_at",
                table: "storage_report_snapshots",
                column: "taken_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "storage_report_snapshots");
        }
    }
}
