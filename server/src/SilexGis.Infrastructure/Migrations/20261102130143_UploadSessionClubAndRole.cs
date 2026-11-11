using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UploadSessionClubAndRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "attach_role",
                table: "upload_sessions",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "caving_group_id",
                table: "upload_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_upload_sessions_caving_group_id",
                table: "upload_sessions",
                column: "caving_group_id",
                filter: "caving_group_id is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_upload_sessions_caving_group_id",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "attach_role",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "caving_group_id",
                table: "upload_sessions");
        }
    }
}
