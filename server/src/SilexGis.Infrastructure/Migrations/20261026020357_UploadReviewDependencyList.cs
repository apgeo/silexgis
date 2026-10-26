using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UploadReviewDependencyList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A held line can depend on more than one held line, so the single pointer becomes a
            // list. Added and filled before the pointer goes, so a waiting line keeps what it
            // depended on.
            migrationBuilder.AddColumn<List<long>>(
                name: "depends_on_item_ids",
                table: "import_batch_items",
                type: "bigint[]",
                nullable: true);

            migrationBuilder.Sql(
                "update import_batch_items set depends_on_item_ids = array[depends_on_item_id] where depends_on_item_id is not null;");

            migrationBuilder.DropForeignKey(
                name: "fk_import_batch_items_import_batch_items_depends_on_item_id",
                table: "import_batch_items");

            migrationBuilder.DropIndex(
                name: "ix_import_batch_items_depends_on_item_id",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "depends_on_item_id",
                table: "import_batch_items");

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_depends_on_item_ids",
                table: "import_batch_items",
                column: "depends_on_item_ids",
                filter: "depends_on_item_ids is not null")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_import_batch_items_depends_on_item_ids",
                table: "import_batch_items");

            migrationBuilder.AddColumn<long>(
                name: "depends_on_item_id",
                table: "import_batch_items",
                type: "bigint",
                nullable: true);

            // The pointer can hold one dependency; the first is kept.
            migrationBuilder.Sql(
                "update import_batch_items set depends_on_item_id = depends_on_item_ids[1] where depends_on_item_ids is not null;");

            migrationBuilder.DropColumn(
                name: "depends_on_item_ids",
                table: "import_batch_items");

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_depends_on_item_id",
                table: "import_batch_items",
                column: "depends_on_item_id",
                filter: "depends_on_item_id is not null");

            migrationBuilder.AddForeignKey(
                name: "fk_import_batch_items_import_batch_items_depends_on_item_id",
                table: "import_batch_items",
                column: "depends_on_item_id",
                principalTable: "import_batch_items",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
