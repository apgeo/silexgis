using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TripParticipantRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trip_participant_roles",
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
                    table.PrimaryKey("pk_trip_participant_roles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_trip_participant_roles_code",
                table: "trip_participant_roles",
                column: "code",
                unique: true);

            // The two roles every existing row is one of, inserted here rather than left to the
            // startup seed, because the column below cannot be filled without them and a row
            // whose role is unknown is a person whose part in the trip has been lost. The seed
            // matches on code and so adds neither of them twice; the rest of the vocabulary,
            // and the wording of these two, remain the seed's business.
            migrationBuilder.Sql(
                """
                INSERT INTO trip_participant_roles (code, name, sort_order, created_at, updated_at)
                VALUES ('participant', 'Participant', 10, now(), now()),
                       ('proposer', 'Proposer', 20, now(), now())
                ON CONFLICT (code) DO NOTHING;
                """);

            migrationBuilder.AddColumn<long>(
                name: "role_id",
                table: "trip_log_participants",
                type: "bigint",
                nullable: true);

            // Attendance was 0 and proposing was 1 while this was a two-value column.
            migrationBuilder.Sql(
                """
                UPDATE trip_log_participants p
                SET role_id = r.id
                FROM trip_participant_roles r
                WHERE r.code = CASE p.kind WHEN 1 THEN 'proposer' ELSE 'participant' END;
                """);

            migrationBuilder.Sql(
                "ALTER TABLE trip_log_participants ALTER COLUMN role_id SET NOT NULL;");

            migrationBuilder.DropIndex(
                name: "ix_trip_log_participants_trip_log_id_kind_caver_id",
                table: "trip_log_participants");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "trip_log_participants");

            migrationBuilder.CreateIndex(
                name: "ix_trip_log_participants_role_id",
                table: "trip_log_participants",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_log_participants_trip_log_id_role_id_caver_id",
                table: "trip_log_participants",
                columns: new[] { "trip_log_id", "role_id", "caver_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_trip_log_participants_trip_participant_roles_role_id",
                table: "trip_log_participants",
                column: "role_id",
                principalTable: "trip_participant_roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_trip_log_participants_trip_participant_roles_role_id",
                table: "trip_log_participants");

            migrationBuilder.DropIndex(
                name: "ix_trip_log_participants_role_id",
                table: "trip_log_participants");

            migrationBuilder.DropIndex(
                name: "ix_trip_log_participants_trip_log_id_role_id_caver_id",
                table: "trip_log_participants");

            migrationBuilder.AddColumn<short>(
                name: "kind",
                table: "trip_log_participants",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            // Only the two roles the old column could express survive going back. Anybody
            // recorded in a role it never had — a leader, a driver — is dropped rather than
            // silently rewritten into an attendance the trip never claimed.
            migrationBuilder.Sql(
                """
                UPDATE trip_log_participants p
                SET kind = 1
                FROM trip_participant_roles r
                WHERE r.id = p.role_id AND r.code = 'proposer';

                DELETE FROM trip_log_participants p
                USING trip_participant_roles r
                WHERE r.id = p.role_id AND r.code NOT IN ('participant', 'proposer');
                """);

            migrationBuilder.DropColumn(
                name: "role_id",
                table: "trip_log_participants");

            migrationBuilder.DropTable(
                name: "trip_participant_roles");

            migrationBuilder.CreateIndex(
                name: "ix_trip_log_participants_trip_log_id_kind_caver_id",
                table: "trip_log_participants",
                columns: new[] { "trip_log_id", "kind", "caver_id" },
                unique: true);
        }
    }
}
