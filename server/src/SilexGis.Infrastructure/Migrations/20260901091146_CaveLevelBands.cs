using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CaveLevelBands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cave_level_bands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cave_feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    bands = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cave_level_bands", x => x.id);
                    table.ForeignKey(
                        name: "fk_cave_level_bands_features_cave_feature_id",
                        column: x => x.cave_feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cave_level_bands_users_confirmed_by",
                        column: x => x.confirmed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cave_level_bands_cave",
                table: "cave_level_bands",
                column: "cave_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_cave_level_bands_confirmed_by",
                table: "cave_level_bands",
                column: "confirmed_by");

            migrationBuilder.CreateIndex(
                name: "ux_cave_level_bands_current",
                table: "cave_level_bands",
                column: "cave_feature_id",
                unique: true,
                filter: "superseded_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cave_level_bands");
        }
    }
}
