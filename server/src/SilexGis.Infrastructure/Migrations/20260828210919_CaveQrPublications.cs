using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CaveQrPublications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cave_qr_publications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_by = table.Column<Guid>(type: "uuid", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cave_qr_publications", x => x.id);
                    table.ForeignKey(
                        name: "fk_cave_qr_publications_features_feature_id",
                        column: x => x.feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cave_qr_publications_users_published_by",
                        column: x => x.published_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cave_qr_publications_feature",
                table: "cave_qr_publications",
                column: "feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_cave_qr_publications_published_by",
                table: "cave_qr_publications",
                column: "published_by");

            migrationBuilder.CreateIndex(
                name: "ux_cave_qr_publications_live",
                table: "cave_qr_publications",
                column: "feature_id",
                unique: true,
                filter: "revoked_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cave_qr_publications");
        }
    }
}
