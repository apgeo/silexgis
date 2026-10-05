using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TripReportTemplateTripType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "trip_type_id",
                table: "trip_report_templates",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_trip_report_templates_trip_type",
                table: "trip_report_templates",
                column: "trip_type_id",
                unique: true,
                filter: "trip_type_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_trip_report_templates_trip_types_trip_type_id",
                table: "trip_report_templates",
                column: "trip_type_id",
                principalTable: "trip_types",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_trip_report_templates_trip_types_trip_type_id",
                table: "trip_report_templates");

            migrationBuilder.DropIndex(
                name: "ux_trip_report_templates_trip_type",
                table: "trip_report_templates");

            migrationBuilder.DropColumn(
                name: "trip_type_id",
                table: "trip_report_templates");
        }
    }
}
