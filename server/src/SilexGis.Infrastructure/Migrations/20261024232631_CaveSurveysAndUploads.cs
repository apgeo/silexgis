using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CaveSurveysAndUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cave_survey_id",
                table: "survey_sources",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "survey_upload_id",
                table: "survey_sources",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cave_survey_id",
                table: "survey_models",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "survey_upload_id",
                table: "survey_models",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cave_surveys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cave_feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    source = table.Column<short>(type: "smallint", nullable: false),
                    external_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cave_surveys", x => x.id);
                    table.ForeignKey(
                        name: "fk_cave_surveys_caves_cave_feature_id",
                        column: x => x.cave_feature_id,
                        principalTable: "caves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_survey_upload_preferences",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    always_outputs = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    public_links = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_survey_upload_preferences", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_survey_upload_preferences_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "survey_uploads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cave_feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cave_survey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    upload_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<short>(type: "smallint", nullable: false),
                    client_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    client_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    survey_title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    survey_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    version_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_cave = table.Column<bool>(type: "boolean", nullable: false),
                    documents_visibility = table.Column<short>(type: "smallint", nullable: true),
                    options = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    survey_date = table.Column<DateOnly>(type: "date", nullable: true),
                    team = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    declination = table.Column<double>(type: "double precision", nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    stage = table.Column<short>(type: "smallint", nullable: true),
                    progress = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    log_tail = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    compiler_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_uploads", x => x.id);
                    table.CheckConstraint("ck_survey_uploads_progress_range", "progress >= 0 and progress <= 100");
                    table.ForeignKey(
                        name: "fk_survey_uploads_cave_surveys_cave_survey_id",
                        column: x => x.cave_survey_id,
                        principalTable: "cave_surveys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_survey_uploads_caves_cave_feature_id",
                        column: x => x.cave_feature_id,
                        principalTable: "caves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_survey_uploads_upload_batches_upload_batch_id",
                        column: x => x.upload_batch_id,
                        principalTable: "upload_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_survey_uploads_users_requested_by",
                        column: x => x.requested_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "survey_upload_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_upload_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    role = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    declared_size = table.Column<long>(type: "bigint", nullable: false),
                    received_size = table.Column<long>(type: "bigint", nullable: true),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    refusal_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    survey_source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    survey_model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_upload_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_survey_upload_files_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_survey_upload_files_survey_models_survey_model_id",
                        column: x => x.survey_model_id,
                        principalTable: "survey_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_survey_upload_files_survey_sources_survey_source_id",
                        column: x => x.survey_source_id,
                        principalTable: "survey_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_survey_upload_files_survey_uploads_survey_upload_id",
                        column: x => x.survey_upload_id,
                        principalTable: "survey_uploads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_survey_sources_cave_survey_id",
                table: "survey_sources",
                column: "cave_survey_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_sources_survey_upload_id",
                table: "survey_sources",
                column: "survey_upload_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_models_cave_survey_id",
                table: "survey_models",
                column: "cave_survey_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_models_survey_upload_id",
                table: "survey_models",
                column: "survey_upload_id");

            migrationBuilder.CreateIndex(
                name: "ix_cave_surveys_cave_feature_id",
                table: "cave_surveys",
                column: "cave_feature_id");

            migrationBuilder.CreateIndex(
                name: "ux_cave_surveys_external_key",
                table: "cave_surveys",
                columns: new[] { "cave_feature_id", "source", "external_key" },
                unique: true,
                filter: "external_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_survey_upload_files_document_id",
                table: "survey_upload_files",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_upload_files_survey_model_id",
                table: "survey_upload_files",
                column: "survey_model_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_upload_files_survey_source_id",
                table: "survey_upload_files",
                column: "survey_source_id");

            migrationBuilder.CreateIndex(
                name: "ux_survey_upload_files_name",
                table: "survey_upload_files",
                columns: new[] { "survey_upload_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_survey_uploads_cave_feature_id",
                table: "survey_uploads",
                column: "cave_feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_uploads_receiving_expires_at",
                table: "survey_uploads",
                column: "expires_at",
                filter: "status = 0");

            migrationBuilder.CreateIndex(
                name: "ix_survey_uploads_requested_by_created_at",
                table: "survey_uploads",
                columns: new[] { "requested_by", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_survey_uploads_upload_batch_id",
                table: "survey_uploads",
                column: "upload_batch_id");

            migrationBuilder.CreateIndex(
                name: "ux_survey_uploads_version",
                table: "survey_uploads",
                columns: new[] { "cave_survey_id", "version_at" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_survey_models_cave_surveys_cave_survey_id",
                table: "survey_models",
                column: "cave_survey_id",
                principalTable: "cave_surveys",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_survey_models_survey_uploads_survey_upload_id",
                table: "survey_models",
                column: "survey_upload_id",
                principalTable: "survey_uploads",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_survey_sources_cave_surveys_cave_survey_id",
                table: "survey_sources",
                column: "cave_survey_id",
                principalTable: "cave_surveys",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_survey_sources_survey_uploads_survey_upload_id",
                table: "survey_sources",
                column: "survey_upload_id",
                principalTable: "survey_uploads",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_survey_models_cave_surveys_cave_survey_id",
                table: "survey_models");

            migrationBuilder.DropForeignKey(
                name: "fk_survey_models_survey_uploads_survey_upload_id",
                table: "survey_models");

            migrationBuilder.DropForeignKey(
                name: "fk_survey_sources_cave_surveys_cave_survey_id",
                table: "survey_sources");

            migrationBuilder.DropForeignKey(
                name: "fk_survey_sources_survey_uploads_survey_upload_id",
                table: "survey_sources");

            migrationBuilder.DropTable(
                name: "survey_upload_files");

            migrationBuilder.DropTable(
                name: "user_survey_upload_preferences");

            migrationBuilder.DropTable(
                name: "survey_uploads");

            migrationBuilder.DropTable(
                name: "cave_surveys");

            migrationBuilder.DropIndex(
                name: "ix_survey_sources_cave_survey_id",
                table: "survey_sources");

            migrationBuilder.DropIndex(
                name: "ix_survey_sources_survey_upload_id",
                table: "survey_sources");

            migrationBuilder.DropIndex(
                name: "ix_survey_models_cave_survey_id",
                table: "survey_models");

            migrationBuilder.DropIndex(
                name: "ix_survey_models_survey_upload_id",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "cave_survey_id",
                table: "survey_sources");

            migrationBuilder.DropColumn(
                name: "survey_upload_id",
                table: "survey_sources");

            migrationBuilder.DropColumn(
                name: "cave_survey_id",
                table: "survey_models");

            migrationBuilder.DropColumn(
                name: "survey_upload_id",
                table: "survey_models");
        }
    }
}
