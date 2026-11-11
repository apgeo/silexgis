using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GeoreferencedMapSheets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "embedded_placement",
                table: "georeferenced_maps",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "master_file_id",
                table: "georeferenced_maps",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "placement",
                table: "georeferenced_maps",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "placement_rms_metres",
                table: "georeferenced_maps",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "placement_source",
                table: "georeferenced_maps",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "preview_file_id",
                table: "georeferenced_maps",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "render_dpi",
                table: "georeferenced_maps",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "render_height",
                table: "georeferenced_maps",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "render_layers",
                table: "georeferenced_maps",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "render_warnings",
                table: "georeferenced_maps",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "render_width",
                table: "georeferenced_maps",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_file_id",
                table: "georeferenced_maps",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_layers",
                table: "georeferenced_maps",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_page",
                table: "georeferenced_maps",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_page_count",
                table: "georeferenced_maps",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_georeferenced_maps_master_file_id",
                table: "georeferenced_maps",
                column: "master_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_georeferenced_maps_preview_file_id",
                table: "georeferenced_maps",
                column: "preview_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_georeferenced_maps_source_file_id",
                table: "georeferenced_maps",
                column: "source_file_id");

            migrationBuilder.AddForeignKey(
                name: "fk_georeferenced_maps_stored_files_master_file_id",
                table: "georeferenced_maps",
                column: "master_file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_georeferenced_maps_stored_files_preview_file_id",
                table: "georeferenced_maps",
                column: "preview_file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_georeferenced_maps_stored_files_source_file_id",
                table: "georeferenced_maps",
                column: "source_file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_georeferenced_maps_stored_files_master_file_id",
                table: "georeferenced_maps");

            migrationBuilder.DropForeignKey(
                name: "fk_georeferenced_maps_stored_files_preview_file_id",
                table: "georeferenced_maps");

            migrationBuilder.DropForeignKey(
                name: "fk_georeferenced_maps_stored_files_source_file_id",
                table: "georeferenced_maps");

            migrationBuilder.DropIndex(
                name: "ix_georeferenced_maps_master_file_id",
                table: "georeferenced_maps");

            migrationBuilder.DropIndex(
                name: "ix_georeferenced_maps_preview_file_id",
                table: "georeferenced_maps");

            migrationBuilder.DropIndex(
                name: "ix_georeferenced_maps_source_file_id",
                table: "georeferenced_maps");

            migrationBuilder.DropColumn(
                name: "embedded_placement",
                table: "georeferenced_maps");

            migrationBuilder.DropColumn(
                name: "master_file_id",
                table: "georeferenced_maps");

            migrationBuilder.DropColumn(
                name: "placement",
                table: "georeferenced_maps");

            migrationBuilder.DropColumn(
                name: "placement_rms_metres",
                table: "georeferenced_maps");

            migrationBuilder.DropColumn(
                name: "placement_source",
                table: "georeferenced_maps");

            migrationBuilder.DropColumn(
                name: "preview_file_id",
                table: "georeferenced_maps");

            migrationBuilder.DropColumn(
                name: "render_dpi",
                table: "georeferenced_maps");

            migrationBuilder.DropColumn(
                name: "render_height",
                table: "georeferenced_maps");

            migrationBuilder.DropColumn(
                name: "render_layers",
                table: "georeferenced_maps");

            migrationBuilder.DropColumn(
                name: "render_warnings",
                table: "georeferenced_maps");

            migrationBuilder.DropColumn(
                name: "render_width",
                table: "georeferenced_maps");

            migrationBuilder.DropColumn(
                name: "source_file_id",
                table: "georeferenced_maps");

            migrationBuilder.DropColumn(
                name: "source_layers",
                table: "georeferenced_maps");

            migrationBuilder.DropColumn(
                name: "source_page",
                table: "georeferenced_maps");

            migrationBuilder.DropColumn(
                name: "source_page_count",
                table: "georeferenced_maps");
        }
    }
}
