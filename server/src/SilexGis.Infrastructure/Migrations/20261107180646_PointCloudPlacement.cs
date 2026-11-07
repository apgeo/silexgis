using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PointCloudPlacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "placement_heading_deg",
                table: "survey_models",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "placement_source",
                table: "survey_models",
                type: "smallint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "placement_heading_deg",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "placement_source",
                table: "survey_models");
        }
    }
}
