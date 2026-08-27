using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NotificationDeliverySegments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "segments",
                table: "notification_deliveries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Rows written before there was an amount on them carry no text to count, and they
            // are still money the day committed. They take the same assumption a row whose text
            // does not exist yet takes — two pieces, which is what every wording that can leave
            // as a text costs at its worst — rather than nothing, because a ceiling reading them
            // as free would under-count in exactly the direction this column exists to correct.
            // The channel and the amount are written out because a migration must keep saying
            // what it said on the day it ran, whatever the code goes on to call them.
            migrationBuilder.Sql("update notification_deliveries set segments = 2 where channel = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "segments",
                table: "notification_deliveries");
        }
    }
}
