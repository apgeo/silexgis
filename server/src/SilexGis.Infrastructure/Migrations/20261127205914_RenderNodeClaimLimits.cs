using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenderNodeClaimLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "skip_needs_bytes",
                table: "render_nodes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "skip_reason",
                table: "render_nodes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "skipped_renders",
                table: "render_nodes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "scratch_bytes",
                table: "flyby_renders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "skip_needs_bytes",
                table: "render_nodes");

            migrationBuilder.DropColumn(
                name: "skip_reason",
                table: "render_nodes");

            migrationBuilder.DropColumn(
                name: "skipped_renders",
                table: "render_nodes");

            migrationBuilder.DropColumn(
                name: "scratch_bytes",
                table: "flyby_renders");
        }
    }
}
