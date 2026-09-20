using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TripTrackingShareExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The default is the beginning of time, which makes every row that predates this column
            // already lapsed. Deliberate and fail-closed: a link minted before publications had an
            // end has no recorded end, and the only safe reading of that is that it has ended. The
            // alternative — picking some window and backdating it — would silently grant a fresh
            // lifetime to exactly the old links this column exists because of. Nothing inserts a row
            // without setting the value, so the column default is only ever reached by a row written
            // by something that forgot to, and a dead link is the right answer for that too.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                table: "trip_tracking_shares",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "trip_tracking_shares");
        }
    }
}
