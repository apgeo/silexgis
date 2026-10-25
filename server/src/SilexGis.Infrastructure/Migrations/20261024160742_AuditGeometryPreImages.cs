using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditGeometryPreImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_geometry_values",
                columns: table => new
                {
                    audit_entry_id = table.Column<long>(type: "bigint", nullable: false),
                    property = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    old_wkb = table.Column<byte[]>(type: "bytea", nullable: true),
                    new_wkb = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_geometry_values", x => new { x.audit_entry_id, x.property });
                    table.ForeignKey(
                        name: "fk_audit_geometry_values_audit_entries_audit_entry_id",
                        column: x => x.audit_entry_id,
                        principalTable: "audit_log",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_geometry_values");
        }
    }
}
