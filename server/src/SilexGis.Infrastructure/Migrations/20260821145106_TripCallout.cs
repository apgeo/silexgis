using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TripCallout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "callout_alarm_at",
                table: "trip_logs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "callout_state",
                table: "trip_logs",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expected_return_at",
                table: "trip_logs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "plan_reminder_sent_at",
                table: "trip_logs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_callout_state_callout_alarm_at",
                table: "trip_logs",
                columns: new[] { "callout_state", "callout_alarm_at" });

            migrationBuilder.CreateIndex(
                name: "ix_processing_jobs_kind_completed_at",
                table: "processing_jobs",
                columns: new[] { "kind", "completed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_trip_logs_callout_state_callout_alarm_at",
                table: "trip_logs");

            migrationBuilder.DropIndex(
                name: "ix_processing_jobs_kind_completed_at",
                table: "processing_jobs");

            migrationBuilder.DropColumn(
                name: "callout_alarm_at",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "callout_state",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "expected_return_at",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "plan_reminder_sent_at",
                table: "trip_logs");
        }
    }
}
