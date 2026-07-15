using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditRootColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "root_entity_id",
                table: "audit_log",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "root_entity_type",
                table: "audit_log",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_root_entity_type_root_entity_id_id",
                table: "audit_log",
                columns: new[] { "root_entity_type", "root_entity_id", "id" },
                descending: new[] { false, false, true },
                filter: "root_entity_type IS NOT NULL");

            // Best-effort backfill: point existing child audit rows at their current parent so
            // pre-existing history shows up in the parent timeline. Rows whose child has since
            // been deleted (the child no longer exists to join) stay rootless — accepted, and
            // the join naturally skips them.
            migrationBuilder.Sql("""
                UPDATE audit_log a SET root_entity_type = 'Cave', root_entity_id = e.cave_id::text
                FROM cave_entrances e
                WHERE a.entity_type = 'CaveEntrance' AND a.entity_id = e.id::text AND a.root_entity_type IS NULL;
                """);
            migrationBuilder.Sql("""
                UPDATE audit_log a SET root_entity_type = 'Cave', root_entity_id = cl.cave_id::text
                FROM cave_centerlines cl
                WHERE a.entity_type = 'CaveCenterline' AND a.entity_id = cl.id::text AND a.root_entity_type IS NULL;
                """);
            migrationBuilder.Sql("""
                UPDATE audit_log a SET root_entity_type = 'Cave', root_entity_id = sm.cave_id::text
                FROM survey_models sm
                WHERE a.entity_type = 'SurveyModel' AND a.entity_id = sm.id::text AND a.root_entity_type IS NULL;
                """);
            // Attachments carry a polymorphic (entity_type smallint, entity_id) parent.
            migrationBuilder.Sql("""
                UPDATE audit_log a SET root_entity_id = att.entity_id::text, root_entity_type = CASE att.entity_type
                        WHEN 0 THEN 'Cave' WHEN 1 THEN 'CaveEntrance' WHEN 2 THEN 'SurfaceFeature'
                        WHEN 3 THEN 'TripLog' WHEN 4 THEN 'Team' WHEN 5 THEN 'Geofile'
                        WHEN 6 THEN 'GeoreferencedMap' WHEN 7 THEN 'MapView' END
                FROM attachments att
                WHERE a.entity_type = 'Attachment' AND a.entity_id = att.id::text AND a.root_entity_type IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_log_root_entity_type_root_entity_id_id",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "root_entity_id",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "root_entity_type",
                table: "audit_log");
        }
    }
}
