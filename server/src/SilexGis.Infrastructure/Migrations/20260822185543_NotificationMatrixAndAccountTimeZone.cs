using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NotificationMatrixAndAccountTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A preference row used to be one answer for a whole category and is now one answer
            // for a category on one channel, so no old row can be carried across as itself: the
            // account-level mail switch and summary setting it depended on are being dropped in
            // the same statement. Emptying the table is the honest move, and it costs nothing —
            // an account with no rows reads back as the documented defaults, which are what every
            // one of these rows would otherwise have to be rewritten into.
            migrationBuilder.Sql("DELETE FROM user_notification_preferences;");

            migrationBuilder.DropIndex(
                name: "ix_user_notification_preferences_user_id_category",
                table: "user_notification_preferences");

            migrationBuilder.DropColumn(
                name: "notify_digest",
                table: "users");

            migrationBuilder.DropColumn(
                name: "notify_email_enabled",
                table: "users");

            migrationBuilder.DropColumn(
                name: "enabled",
                table: "user_notification_preferences");

            migrationBuilder.AddColumn<short>(
                name: "channel",
                table: "user_notification_preferences",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<short>(
                name: "choice",
                table: "user_notification_preferences",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.CreateIndex(
                name: "ix_user_notification_preferences_user_id_category_channel",
                table: "user_notification_preferences",
                columns: new[] { "user_id", "category", "channel" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_notification_preferences_one_channel",
                table: "user_notification_preferences",
                sql: "channel > 0 AND (channel & (channel - 1)) = 0");

            // Where the account lives, in the same statement as where it wants to be told things:
            // the rules that read the zone are the ones the matrix routes into, and a schema that
            // arrived in two halves would leave one of them briefly unanswerable.
            migrationBuilder.AddColumn<string>(
                name: "time_zone",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM user_notification_preferences;");

            migrationBuilder.DropIndex(
                name: "ix_user_notification_preferences_user_id_category_channel",
                table: "user_notification_preferences");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_notification_preferences_one_channel",
                table: "user_notification_preferences");

            migrationBuilder.DropColumn(
                name: "channel",
                table: "user_notification_preferences");

            migrationBuilder.DropColumn(
                name: "choice",
                table: "user_notification_preferences");

            migrationBuilder.AddColumn<short>(
                name: "notify_digest",
                table: "users",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<bool>(
                name: "notify_email_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "enabled",
                table: "user_notification_preferences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_user_notification_preferences_user_id_category",
                table: "user_notification_preferences",
                columns: new[] { "user_id", "category" },
                unique: true);

            migrationBuilder.DropColumn(
                name: "time_zone",
                table: "users");
        }
    }
}
