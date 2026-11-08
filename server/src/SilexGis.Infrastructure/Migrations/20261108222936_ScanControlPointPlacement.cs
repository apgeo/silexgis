using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ScanControlPointPlacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "placement_control_points",
                table: "survey_models",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "placement_pitch_deg",
                table: "survey_models",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "placement_rms_m",
                table: "survey_models",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "placement_roll_deg",
                table: "survey_models",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "placement_control_points",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "placement_pitch_deg",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "placement_rms_m",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "placement_roll_deg",
                table: "survey_models");
        }
    }
}
