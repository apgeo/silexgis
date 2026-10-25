using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SurveyUploadPublicLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "public_link_refusal",
                table: "survey_uploads",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "survey_upload_public_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_upload_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    token_protected = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_upload_public_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_survey_upload_public_links_survey_uploads_survey_upload_id",
                        column: x => x.survey_upload_id,
                        principalTable: "survey_uploads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_survey_upload_public_links_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_survey_upload_public_links_created_by",
                table: "survey_upload_public_links",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ux_survey_upload_public_links_live",
                table: "survey_upload_public_links",
                column: "survey_upload_id",
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_survey_upload_public_links_token_hash",
                table: "survey_upload_public_links",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "survey_upload_public_links");

            migrationBuilder.DropColumn(
                name: "public_link_refusal",
                table: "survey_uploads");
        }
    }
}
