using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameReportTemplates : Migration
    {
        // Renames, written by hand in place of the drop-and-create the tooling proposes for a
        // table whose name changed: the rows are a club's own layouts and the change is to what
        // the table is called, not to what it holds. The table was named for trips and has held
        // camp layouts too since a layout gained a kind. The primary key and the foreign key
        // have no rename operation of their own, so those two are stated as statements.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "trip_report_templates",
                newName: "report_templates");

            migrationBuilder.RenameIndex(
                name: "ux_trip_report_templates_default",
                table: "report_templates",
                newName: "ux_report_templates_default");

            migrationBuilder.RenameIndex(
                name: "ux_trip_report_templates_trip_type",
                table: "report_templates",
                newName: "ux_report_templates_trip_type");

            migrationBuilder.Sql(
                "ALTER TABLE report_templates RENAME CONSTRAINT pk_trip_report_templates TO pk_report_templates;");
            migrationBuilder.Sql(
                "ALTER TABLE report_templates RENAME CONSTRAINT fk_trip_report_templates_trip_types_trip_type_id "
                + "TO fk_report_templates_trip_types_trip_type_id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE report_templates RENAME CONSTRAINT fk_report_templates_trip_types_trip_type_id "
                + "TO fk_trip_report_templates_trip_types_trip_type_id;");
            migrationBuilder.Sql(
                "ALTER TABLE report_templates RENAME CONSTRAINT pk_report_templates TO pk_trip_report_templates;");

            migrationBuilder.RenameIndex(
                name: "ux_report_templates_trip_type",
                table: "report_templates",
                newName: "ux_trip_report_templates_trip_type");

            migrationBuilder.RenameIndex(
                name: "ux_report_templates_default",
                table: "report_templates",
                newName: "ux_trip_report_templates_default");

            migrationBuilder.RenameTable(
                name: "report_templates",
                newName: "trip_report_templates");
        }
    }
}
