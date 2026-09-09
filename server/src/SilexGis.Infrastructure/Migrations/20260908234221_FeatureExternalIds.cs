using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FeatureExternalIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feature_external_ids",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    system = table.Column<short>(type: "smallint", nullable: false),
                    value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_external_ids", x => x.id);
                    table.ForeignKey(
                        name: "fk_feature_external_ids_features_feature_id",
                        column: x => x.feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_feature_external_ids_feature_id_system",
                table: "feature_external_ids",
                columns: new[] { "feature_id", "system" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feature_external_ids_system_value",
                table: "feature_external_ids",
                columns: new[] { "system", "value" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feature_external_ids");
        }
    }
}
