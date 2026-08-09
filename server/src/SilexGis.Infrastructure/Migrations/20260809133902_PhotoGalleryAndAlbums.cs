using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PhotoGalleryAndAlbums : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_attachments_entity_type_entity_id",
                table: "attachments");

            migrationBuilder.DropIndex(
                name: "ix_attachments_feature_id",
                table: "attachments");

            migrationBuilder.AddColumn<int>(
                name: "orientation_quarter_turns",
                table: "files",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by_user_id",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_primary",
                table: "attachments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "albums",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cover_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_entity_type = table.Column<short>(type: "smallint", nullable: true),
                    subject_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caving_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_albums", x => x.id);
                    table.CheckConstraint("ck_albums_subject", "(subject_feature_id is null and subject_entity_type is null and subject_entity_id is null) or (subject_feature_id is not null and subject_entity_type is null and subject_entity_id is null) or (subject_feature_id is null and subject_entity_type is not null and subject_entity_id is not null)");
                    table.ForeignKey(
                        name: "fk_albums_caving_groups_caving_group_id",
                        column: x => x.caving_group_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_albums_documents_cover_document_id",
                        column: x => x.cover_document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_albums_features_subject_feature_id",
                        column: x => x.subject_feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_albums_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "photo_details",
                columns: table => new
                {
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    photographer_caver_id = table.Column<Guid>(type: "uuid", nullable: true),
                    photographer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    caption = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    licence_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    place_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    in_public_gallery = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_photo_details", x => x.document_id);
                    table.ForeignKey(
                        name: "fk_photo_details_cavers_photographer_caver_id",
                        column: x => x.photographer_caver_id,
                        principalTable: "cavers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_photo_details_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "album_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    album_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caption = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_album_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_album_items_albums_album_id",
                        column: x => x.album_id,
                        principalTable: "albums",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_album_items_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "album_shares",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    album_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    mode = table.Column<short>(type: "smallint", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_album_shares", x => x.id);
                    table.ForeignKey(
                        name: "fk_album_shares_albums_album_id",
                        column: x => x.album_id,
                        principalTable: "albums",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_files_kind_created_at",
                table: "files",
                columns: new[] { "kind", "created_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_files_orientation",
                table: "files",
                sql: "orientation_quarter_turns between 0 and 3");

            migrationBuilder.CreateIndex(
                name: "ix_documents_deleted_at",
                table: "documents",
                column: "deleted_at",
                filter: "deleted_at is not null");

            migrationBuilder.CreateIndex(
                name: "ix_documents_deleted_by_user_id",
                table: "documents",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_attachments_primary_entity",
                table: "attachments",
                columns: new[] { "entity_type", "entity_id" },
                unique: true,
                filter: "is_primary and entity_id is not null");

            migrationBuilder.CreateIndex(
                name: "ux_attachments_primary_feature",
                table: "attachments",
                column: "feature_id",
                unique: true,
                filter: "is_primary and feature_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_album_items_album_id_document_id",
                table: "album_items",
                columns: new[] { "album_id", "document_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_album_items_album_id_sort_order",
                table: "album_items",
                columns: new[] { "album_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_album_items_document_id",
                table: "album_items",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_album_shares_album_id",
                table: "album_shares",
                column: "album_id");

            migrationBuilder.CreateIndex(
                name: "ix_album_shares_token_hash",
                table: "album_shares",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_albums_caving_group_id",
                table: "albums",
                column: "caving_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_albums_cover_document_id",
                table: "albums",
                column: "cover_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_albums_owner_user_id",
                table: "albums",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_albums_subject_entity_type_subject_entity_id",
                table: "albums",
                columns: new[] { "subject_entity_type", "subject_entity_id" },
                filter: "subject_entity_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_albums_subject_feature_id",
                table: "albums",
                column: "subject_feature_id",
                filter: "subject_feature_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_photo_details_in_public_gallery",
                table: "photo_details",
                column: "in_public_gallery",
                filter: "in_public_gallery");

            migrationBuilder.CreateIndex(
                name: "ix_photo_details_photographer_caver_id",
                table: "photo_details",
                column: "photographer_caver_id",
                filter: "photographer_caver_id is not null");

            migrationBuilder.AddForeignKey(
                name: "fk_documents_users_deleted_by_user_id",
                table: "documents",
                column: "deleted_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_documents_users_deleted_by_user_id",
                table: "documents");

            migrationBuilder.DropTable(
                name: "album_items");

            migrationBuilder.DropTable(
                name: "album_shares");

            migrationBuilder.DropTable(
                name: "photo_details");

            migrationBuilder.DropTable(
                name: "albums");

            migrationBuilder.DropIndex(
                name: "ix_files_kind_created_at",
                table: "files");

            migrationBuilder.DropCheckConstraint(
                name: "ck_files_orientation",
                table: "files");

            migrationBuilder.DropIndex(
                name: "ix_documents_deleted_at",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "ix_documents_deleted_by_user_id",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "ux_attachments_primary_entity",
                table: "attachments");

            migrationBuilder.DropIndex(
                name: "ux_attachments_primary_feature",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "orientation_quarter_turns",
                table: "files");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "deleted_by_user_id",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "is_primary",
                table: "attachments");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_entity_type_entity_id",
                table: "attachments",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_feature_id",
                table: "attachments",
                column: "feature_id");
        }
    }
}
