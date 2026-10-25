using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StagedSyncUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "kind",
                table: "upload_sessions",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mime_type",
                table: "upload_sessions",
                type: "character varying(127)",
                maxLength: 127,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sha256",
                table: "upload_sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "staged_at",
                table: "upload_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "staged_for_sync",
                table: "upload_sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_upload_sessions_staged_described",
                table: "upload_sessions",
                sql: "staged_at is null or (staged_for_sync and received_bytes = declared_size_bytes and sha256 is not null and mime_type is not null and kind is not null)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_upload_sessions_staged_described",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "mime_type",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "sha256",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "staged_at",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "staged_for_sync",
                table: "upload_sessions");
        }
    }
}
