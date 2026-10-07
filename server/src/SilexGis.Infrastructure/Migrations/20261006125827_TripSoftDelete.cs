using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TripSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "trip_logs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by_user_id",
                table: "trip_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_deleted_at",
                table: "trip_logs",
                column: "deleted_at",
                filter: "deleted_at is not null");

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_deleted_by_user_id",
                table: "trip_logs",
                column: "deleted_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_trip_logs_users_deleted_by_user_id",
                table: "trip_logs",
                column: "deleted_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_trip_logs_users_deleted_by_user_id",
                table: "trip_logs");

            migrationBuilder.DropIndex(
                name: "ix_trip_logs_deleted_at",
                table: "trip_logs");

            migrationBuilder.DropIndex(
                name: "ix_trip_logs_deleted_by_user_id",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "deleted_by_user_id",
                table: "trip_logs");
        }
    }
}
