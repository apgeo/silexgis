using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SurveyUploadLinkAudiences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "default_link_audience",
                table: "user_survey_upload_preferences",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<bool>(
                name: "public_link_allow_protected",
                table: "survey_uploads",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<short>(
                name: "public_link_audience",
                table: "survey_uploads",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "public_link_wanted",
                table: "survey_uploads",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "audience",
                table: "survey_upload_public_links",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "protected_acknowledged_at",
                table: "survey_upload_public_links",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "protected_acknowledged_by",
                table: "survey_upload_public_links",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_survey_upload_public_links_protected_acknowledged_by",
                table: "survey_upload_public_links",
                column: "protected_acknowledged_by");

            migrationBuilder.AddCheckConstraint(
                name: "ck_survey_upload_public_links_acknowledgement",
                table: "survey_upload_public_links",
                sql: "(protected_acknowledged_by IS NULL) = (protected_acknowledged_at IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_survey_upload_public_links_users_protected_acknowledged_by",
                table: "survey_upload_public_links",
                column: "protected_acknowledged_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_survey_upload_public_links_users_protected_acknowledged_by",
                table: "survey_upload_public_links");

            migrationBuilder.DropIndex(
                name: "ix_survey_upload_public_links_protected_acknowledged_by",
                table: "survey_upload_public_links");

            migrationBuilder.DropCheckConstraint(
                name: "ck_survey_upload_public_links_acknowledgement",
                table: "survey_upload_public_links");

            migrationBuilder.DropColumn(
                name: "default_link_audience",
                table: "user_survey_upload_preferences");

            migrationBuilder.DropColumn(
                name: "public_link_allow_protected",
                table: "survey_uploads");

            migrationBuilder.DropColumn(
                name: "public_link_audience",
                table: "survey_uploads");

            migrationBuilder.DropColumn(
                name: "public_link_wanted",
                table: "survey_uploads");

            migrationBuilder.DropColumn(
                name: "audience",
                table: "survey_upload_public_links");

            migrationBuilder.DropColumn(
                name: "protected_acknowledged_at",
                table: "survey_upload_public_links");

            migrationBuilder.DropColumn(
                name: "protected_acknowledged_by",
                table: "survey_upload_public_links");
        }
    }
}
