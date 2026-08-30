using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpeditionReportTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_trip_report_templates_default",
                table: "trip_report_templates");

            migrationBuilder.AddColumn<short>(
                name: "kind",
                table: "trip_report_templates",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.CreateIndex(
                name: "ux_trip_report_templates_default",
                table: "trip_report_templates",
                columns: new[] { "kind", "is_default" },
                unique: true,
                filter: "is_default");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_trip_report_templates_default",
                table: "trip_report_templates");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "trip_report_templates");

            migrationBuilder.CreateIndex(
                name: "ux_trip_report_templates_default",
                table: "trip_report_templates",
                column: "is_default",
                unique: true,
                filter: "is_default");
        }
    }
}
