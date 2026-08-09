using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UploadBatchesAndCabinetDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "storage_quota_bytes",
                table: "users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "upload_batch_id",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "default_document_type_id",
                table: "cabinets",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long[]>(
                name: "default_tag_ids",
                table: "cabinets",
                type: "bigint[]",
                nullable: false,
                defaultValueSql: "'{}'::bigint[]");

            migrationBuilder.AddColumn<short>(
                name: "default_visibility",
                table: "cabinets",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "required_metadata_keys",
                table: "cabinets",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.CreateTable(
                name: "duplicate_upload_records",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    new_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    existing_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_duplicate_upload_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "upload_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    started_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cabinet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tag_id = table.Column<long>(type: "bigint", nullable: true),
                    source_description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    total_count = table.Column<int>(type: "integer", nullable: false),
                    stored_count = table.Column<int>(type: "integer", nullable: false),
                    skipped_count = table.Column<int>(type: "integer", nullable: false),
                    failed_count = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_upload_batches", x => x.id);
                    table.ForeignKey(
                        name: "fk_upload_batches_cabinets_cabinet_id",
                        column: x => x.cabinet_id,
                        principalTable: "cabinets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_upload_batches_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_upload_batches_users_started_by_user_id",
                        column: x => x.started_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "upload_batch_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    upload_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_path = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    outcome = table.Column<short>(type: "smallint", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cabinet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    duplicate_of_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_upload_batch_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_upload_batch_items_upload_batches_upload_batch_id",
                        column: x => x.upload_batch_id,
                        principalTable: "upload_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "upload_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    declared_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    received_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    cabinet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    upload_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attach_entity_type = table.Column<short>(type: "smallint", nullable: true),
                    attach_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attach_feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    relative_path = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_upload_sessions", x => x.id);
                    table.CheckConstraint("ck_upload_sessions_received_within_declared", "received_bytes >= 0 and received_bytes <= declared_size_bytes");
                    table.ForeignKey(
                        name: "fk_upload_sessions_cabinets_cabinet_id",
                        column: x => x.cabinet_id,
                        principalTable: "cabinets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_upload_sessions_upload_batches_upload_batch_id",
                        column: x => x.upload_batch_id,
                        principalTable: "upload_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_upload_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_documents_upload_batch_id",
                table: "documents",
                column: "upload_batch_id",
                filter: "upload_batch_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_cabinets_default_document_type_id",
                table: "cabinets",
                column: "default_document_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_upload_records_created_at",
                table: "duplicate_upload_records",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_upload_records_sha256",
                table: "duplicate_upload_records",
                column: "sha256");

            migrationBuilder.CreateIndex(
                name: "ix_upload_batch_items_document_id",
                table: "upload_batch_items",
                column: "document_id",
                filter: "document_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_upload_batch_items_upload_batch_id_id",
                table: "upload_batch_items",
                columns: new[] { "upload_batch_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_upload_batches_cabinet_id",
                table: "upload_batches",
                column: "cabinet_id");

            migrationBuilder.CreateIndex(
                name: "ix_upload_batches_started_by_user_id_created_at",
                table: "upload_batches",
                columns: new[] { "started_by_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_upload_batches_status",
                table: "upload_batches",
                column: "status",
                filter: "status in (0, 1)");

            migrationBuilder.CreateIndex(
                name: "ix_upload_batches_tag_id",
                table: "upload_batches",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_upload_sessions_cabinet_id",
                table: "upload_sessions",
                column: "cabinet_id");

            migrationBuilder.CreateIndex(
                name: "ix_upload_sessions_expires_at",
                table: "upload_sessions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_upload_sessions_upload_batch_id",
                table: "upload_sessions",
                column: "upload_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_upload_sessions_user_id",
                table: "upload_sessions",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_cabinets_document_types_default_document_type_id",
                table: "cabinets",
                column: "default_document_type_id",
                principalTable: "document_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_documents_upload_batches_upload_batch_id",
                table: "documents",
                column: "upload_batch_id",
                principalTable: "upload_batches",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_cabinets_document_types_default_document_type_id",
                table: "cabinets");

            migrationBuilder.DropForeignKey(
                name: "fk_documents_upload_batches_upload_batch_id",
                table: "documents");

            migrationBuilder.DropTable(
                name: "duplicate_upload_records");

            migrationBuilder.DropTable(
                name: "upload_batch_items");

            migrationBuilder.DropTable(
                name: "upload_sessions");

            migrationBuilder.DropTable(
                name: "upload_batches");

            migrationBuilder.DropIndex(
                name: "ix_documents_upload_batch_id",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "ix_cabinets_default_document_type_id",
                table: "cabinets");

            migrationBuilder.DropColumn(
                name: "storage_quota_bytes",
                table: "users");

            migrationBuilder.DropColumn(
                name: "upload_batch_id",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "default_document_type_id",
                table: "cabinets");

            migrationBuilder.DropColumn(
                name: "default_tag_ids",
                table: "cabinets");

            migrationBuilder.DropColumn(
                name: "default_visibility",
                table: "cabinets");

            migrationBuilder.DropColumn(
                name: "required_metadata_keys",
                table: "cabinets");
        }
    }
}
