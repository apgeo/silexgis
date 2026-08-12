using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TripTypeTaxonomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "type",
                table: "trip_logs");

            migrationBuilder.AddColumn<long>(
                name: "trip_type_id",
                table: "trip_logs",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "trip_types",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_types", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_trip_type_id",
                table: "trip_logs",
                column: "trip_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_types_code",
                table: "trip_types",
                column: "code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_trip_logs_trip_types_trip_type_id",
                table: "trip_logs",
                column: "trip_type_id",
                principalTable: "trip_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_trip_logs_trip_types_trip_type_id",
                table: "trip_logs");

            migrationBuilder.DropTable(
                name: "trip_types");

            migrationBuilder.DropIndex(
                name: "ix_trip_logs_trip_type_id",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "trip_type_id",
                table: "trip_logs");

            migrationBuilder.AddColumn<short>(
                name: "type",
                table: "trip_logs",
                type: "smallint",
                nullable: true);
        }
    }
}
