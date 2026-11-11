using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UsableLookupIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_features_deleted_at",
                table: "features");

            migrationBuilder.CreateIndex(
                name: "ix_taggings_tag_id",
                table: "taggings",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_upload_public_links_survey_upload_id",
                table: "survey_upload_public_links",
                column: "survey_upload_id");

            migrationBuilder.CreateIndex(
                name: "ix_features_deleted_at_not_null",
                table: "features",
                column: "deleted_at",
                filter: "deleted_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_feature_hierarchy_edges_child_id",
                table: "feature_hierarchy_edges",
                column: "child_id");

            migrationBuilder.CreateIndex(
                name: "ix_centerlines_cave_feature_id",
                table: "centerlines",
                column: "cave_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_entity_type_entity_id",
                table: "attachments",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_feature_id",
                table: "attachments",
                column: "feature_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_taggings_tag_id",
                table: "taggings");

            migrationBuilder.DropIndex(
                name: "ix_survey_upload_public_links_survey_upload_id",
                table: "survey_upload_public_links");

            migrationBuilder.DropIndex(
                name: "ix_features_deleted_at_not_null",
                table: "features");

            migrationBuilder.DropIndex(
                name: "ix_feature_hierarchy_edges_child_id",
                table: "feature_hierarchy_edges");

            migrationBuilder.DropIndex(
                name: "ix_centerlines_cave_feature_id",
                table: "centerlines");

            migrationBuilder.DropIndex(
                name: "ix_attachments_entity_type_entity_id",
                table: "attachments");

            migrationBuilder.DropIndex(
                name: "ix_attachments_feature_id",
                table: "attachments");

            migrationBuilder.CreateIndex(
                name: "ix_features_deleted_at",
                table: "features",
                column: "deleted_at",
                filter: "deleted_at IS NULL");
        }
    }
}
