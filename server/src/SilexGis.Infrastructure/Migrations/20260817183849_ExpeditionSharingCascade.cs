using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpeditionSharingCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "granted_via_expedition_id",
                table: "access_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_access_entries_granted_via_expedition_id",
                table: "access_entries",
                column: "granted_via_expedition_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_entries_scope_id",
                table: "access_entries",
                column: "scope_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_access_entries_granted_via_expedition",
                table: "access_entries",
                sql: "granted_via_expedition_id IS NULL OR (domain = 1 AND scope_kind = 5)");

            migrationBuilder.AddForeignKey(
                name: "fk_access_entries_expeditions_granted_via_expedition_id",
                table: "access_entries",
                column: "granted_via_expedition_id",
                principalTable: "expeditions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_access_entries_expeditions_granted_via_expedition_id",
                table: "access_entries");

            migrationBuilder.DropIndex(
                name: "ix_access_entries_granted_via_expedition_id",
                table: "access_entries");

            migrationBuilder.DropIndex(
                name: "ix_access_entries_scope_id",
                table: "access_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_access_entries_granted_via_expedition",
                table: "access_entries");

            migrationBuilder.DropColumn(
                name: "granted_via_expedition_id",
                table: "access_entries");
        }
    }
}
