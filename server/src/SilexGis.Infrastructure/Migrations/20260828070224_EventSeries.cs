using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EventSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "series_id",
                table: "events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "series_rule",
                table: "events",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_events_series_id_start_date",
                table: "events",
                columns: new[] { "series_id", "start_date" },
                filter: "series_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_events_series_rule",
                table: "events",
                sql: "series_rule IS NULL OR series_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_events_series_id_start_date",
                table: "events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_events_series_rule",
                table: "events");

            migrationBuilder.DropColumn(
                name: "series_id",
                table: "events");

            migrationBuilder.DropColumn(
                name: "series_rule",
                table: "events");
        }
    }
}
