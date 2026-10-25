using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DevicePersons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "device_person_id",
                table: "import_batch_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "device_persons",
                columns: table => new
                {
                    device_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    first_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    last_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    caver_id = table.Column<Guid>(type: "uuid", nullable: true),
                    link_source = table.Column<short>(type: "smallint", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_persons", x => x.device_user_id);
                    table.ForeignKey(
                        name: "fk_device_persons_cavers_caver_id",
                        column: x => x.caver_id,
                        principalTable: "cavers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_device_persons_users_uploaded_by_user_id",
                        column: x => x.uploaded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_device_person_id",
                table: "import_batch_items",
                column: "device_person_id",
                filter: "device_person_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_device_persons_caver_id",
                table: "device_persons",
                column: "caver_id",
                filter: "caver_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_device_persons_uploaded_by_user_id",
                table: "device_persons",
                column: "uploaded_by_user_id",
                filter: "uploaded_by_user_id is not null");

            migrationBuilder.AddForeignKey(
                name: "fk_import_batch_items_device_persons_device_person_id",
                table: "import_batch_items",
                column: "device_person_id",
                principalTable: "device_persons",
                principalColumn: "device_user_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_import_batch_items_device_persons_device_person_id",
                table: "import_batch_items");

            migrationBuilder.DropTable(
                name: "device_persons");

            migrationBuilder.DropIndex(
                name: "ix_import_batch_items_device_person_id",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "device_person_id",
                table: "import_batch_items");
        }
    }
}
