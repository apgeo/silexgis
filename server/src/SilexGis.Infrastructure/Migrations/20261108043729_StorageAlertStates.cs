using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StorageAlertStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "storage_alert_states",
                columns: table => new
                {
                    limit_kind = table.Column<short>(type: "smallint", nullable: false),
                    level = table.Column<short>(type: "smallint", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_alert_states", x => x.limit_kind);
                    table.CheckConstraint("ck_storage_alert_states_level", "level >= 0 AND level <= 3");
                    table.CheckConstraint("ck_storage_alert_states_limit_kind", "limit_kind >= 0 AND limit_kind <= 4");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "storage_alert_states");
        }
    }
}
