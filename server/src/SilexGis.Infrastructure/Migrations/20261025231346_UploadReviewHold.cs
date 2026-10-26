using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UploadReviewHold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "uploads_need_review",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                table: "import_batches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decided_by_user_id",
                table: "import_batches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "held_at",
                table: "import_batches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "held_request",
                table: "import_batches",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_note",
                table: "import_batches",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "review_state",
                table: "import_batches",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<List<string>>(
                name: "accepted_fields",
                table: "import_batch_items",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "applied_at",
                table: "import_batch_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                table: "import_batch_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decided_by_user_id",
                table: "import_batch_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision_note",
                table: "import_batch_items",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "depends_on_item_id",
                table: "import_batch_items",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "proposal",
                table: "import_batch_items",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "proposal_row_id",
                table: "import_batch_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "proposal_section",
                table: "import_batch_items",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "review_decision",
                table: "import_batch_items",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "review_id",
                table: "import_batch_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_decided_by_user_id",
                table: "import_batches",
                column: "decided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_review_state_held_at",
                table: "import_batches",
                columns: new[] { "review_state", "held_at" },
                filter: "review_state <> 0");

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_decided_by_user_id",
                table: "import_batch_items",
                column: "decided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_depends_on_item_id",
                table: "import_batch_items",
                column: "depends_on_item_id",
                filter: "depends_on_item_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_pending_row",
                table: "import_batch_items",
                columns: new[] { "proposal_row_id", "proposal_section" },
                filter: "review_decision = 0");

            migrationBuilder.CreateIndex(
                name: "ux_import_batch_items_review_id",
                table: "import_batch_items",
                column: "review_id",
                unique: true,
                filter: "review_id is not null");

            migrationBuilder.AddForeignKey(
                name: "fk_import_batch_items_import_batch_items_depends_on_item_id",
                table: "import_batch_items",
                column: "depends_on_item_id",
                principalTable: "import_batch_items",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_import_batch_items_users_decided_by_user_id",
                table: "import_batch_items",
                column: "decided_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_import_batches_users_decided_by_user_id",
                table: "import_batches",
                column: "decided_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // The upload-review right is seeded to the Administrators group, but the seeder only
            // ever fills a group it creates: on a database whose groups exist already the new
            // domain would never arrive, and nobody but a full administrator could review. So it
            // is carried over here, once, at the moment the domain appears — every action at the
            // whole-installation scope, as the seeder gives the group every other domain — and
            // only where the group exists and holds no entry for the domain yet. On a fresh
            // database the group does not exist yet and the seeder creates it with the entry.
            migrationBuilder.Sql("""
                insert into access_entries (permission_group_id, effect, domain, actions, scope_kind, created_at, updated_at)
                select g.id, 0, 24, 255, 0, now(), now()
                from permission_groups g
                where g.slug = 'administrators'
                  and not exists (
                      select 1 from access_entries e where e.permission_group_id = g.id and e.domain = 24);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("delete from access_entries where domain = 24;");

            migrationBuilder.DropForeignKey(
                name: "fk_import_batch_items_import_batch_items_depends_on_item_id",
                table: "import_batch_items");

            migrationBuilder.DropForeignKey(
                name: "fk_import_batch_items_users_decided_by_user_id",
                table: "import_batch_items");

            migrationBuilder.DropForeignKey(
                name: "fk_import_batches_users_decided_by_user_id",
                table: "import_batches");

            migrationBuilder.DropIndex(
                name: "ix_import_batches_decided_by_user_id",
                table: "import_batches");

            migrationBuilder.DropIndex(
                name: "ix_import_batches_review_state_held_at",
                table: "import_batches");

            migrationBuilder.DropIndex(
                name: "ix_import_batch_items_decided_by_user_id",
                table: "import_batch_items");

            migrationBuilder.DropIndex(
                name: "ix_import_batch_items_depends_on_item_id",
                table: "import_batch_items");

            migrationBuilder.DropIndex(
                name: "ix_import_batch_items_pending_row",
                table: "import_batch_items");

            migrationBuilder.DropIndex(
                name: "ux_import_batch_items_review_id",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "uploads_need_review",
                table: "users");

            migrationBuilder.DropColumn(
                name: "decided_at",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "decided_by_user_id",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "held_at",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "held_request",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "review_note",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "review_state",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "accepted_fields",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "applied_at",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "decided_at",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "decided_by_user_id",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "decision_note",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "depends_on_item_id",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "proposal",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "proposal_row_id",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "proposal_section",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "review_decision",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "review_id",
                table: "import_batch_items");
        }
    }
}
