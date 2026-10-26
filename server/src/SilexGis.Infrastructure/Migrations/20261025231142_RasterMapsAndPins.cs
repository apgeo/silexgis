using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RasterMapsAndPins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "raster_map_id",
                table: "import_batch_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "raster_pin_id",
                table: "import_batch_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "raster_maps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cave_feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cave_area_feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    map_type = table.Column<short>(type: "smallint", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_raster_maps", x => x.id);
                    table.ForeignKey(
                        name: "fk_raster_maps_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_raster_maps_features_cave_area_feature_id",
                        column: x => x.cave_area_feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_raster_maps_features_cave_feature_id",
                        column: x => x.cave_feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_raster_maps_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "raster_pins",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    raster_map_id = table.Column<Guid>(type: "uuid", nullable: false),
                    place_feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    x = table.Column<int>(type: "integer", nullable: false),
                    y = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_raster_pins", x => x.id);
                    table.ForeignKey(
                        name: "fk_raster_pins_features_place_feature_id",
                        column: x => x.place_feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_raster_pins_raster_maps_raster_map_id",
                        column: x => x.raster_map_id,
                        principalTable: "raster_maps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_raster_map_id",
                table: "import_batch_items",
                column: "raster_map_id",
                filter: "raster_map_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_raster_pin_id",
                table: "import_batch_items",
                column: "raster_pin_id",
                filter: "raster_pin_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_raster_maps_cave_area_feature_id",
                table: "raster_maps",
                column: "cave_area_feature_id",
                filter: "cave_area_feature_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_raster_maps_cave_feature_id",
                table: "raster_maps",
                column: "cave_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_raster_maps_deleted_at",
                table: "raster_maps",
                column: "deleted_at",
                filter: "deleted_at is not null");

            migrationBuilder.CreateIndex(
                name: "ix_raster_maps_document_id",
                table: "raster_maps",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_raster_maps_owner_user_id",
                table: "raster_maps",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_raster_pins_map_updated",
                table: "raster_pins",
                columns: new[] { "raster_map_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_raster_pins_place_feature_id",
                table: "raster_pins",
                column: "place_feature_id");

            migrationBuilder.CreateIndex(
                name: "ux_raster_pins_map_place_live",
                table: "raster_pins",
                columns: new[] { "raster_map_id", "place_feature_id" },
                unique: true,
                filter: "deleted_at is null");

            migrationBuilder.AddForeignKey(
                name: "fk_import_batch_items_raster_maps_raster_map_id",
                table: "import_batch_items",
                column: "raster_map_id",
                principalTable: "raster_maps",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_import_batch_items_raster_pins_raster_pin_id",
                table: "import_batch_items",
                column: "raster_pin_id",
                principalTable: "raster_pins",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_import_batch_items_raster_maps_raster_map_id",
                table: "import_batch_items");

            migrationBuilder.DropForeignKey(
                name: "fk_import_batch_items_raster_pins_raster_pin_id",
                table: "import_batch_items");

            migrationBuilder.DropTable(
                name: "raster_pins");

            migrationBuilder.DropTable(
                name: "raster_maps");

            migrationBuilder.DropIndex(
                name: "ix_import_batch_items_raster_map_id",
                table: "import_batch_items");

            migrationBuilder.DropIndex(
                name: "ix_import_batch_items_raster_pin_id",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "raster_map_id",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "raster_pin_id",
                table: "import_batch_items");
        }
    }
}
