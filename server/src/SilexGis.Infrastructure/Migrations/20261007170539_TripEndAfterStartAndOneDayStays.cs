using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TripEndAfterStartAndOneDayStays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_expedition_roster_dates",
                table: "expedition_roster");

            // A trip stored with a last day equal to its first, from before the write path folded
            // the two, becomes the one-day trip every reader already took it for. One stored with
            // a last day before its first — an import took a mistyped date as written — goes the
            // same way: every reader has ignored such an end rather than obeyed it, so dropping
            // it changes what no surface shows. Deleted trips are included on purpose; the
            // constraint below is over the whole table and a trip put back must satisfy it too.
            migrationBuilder.Sql(
                "UPDATE trip_logs SET trip_date_end = NULL WHERE trip_date_end <= trip_date;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trip_logs_dates",
                table: "trip_logs",
                sql: "trip_date_end IS NULL OR trip_date_end > trip_date");

            // Nothing is rewritten on the roster. A stay stored with no last day was, until now,
            // either one day or somebody still there, and the row does not say which; from here
            // on it means still there, and a stay that was one day is corrected by giving it its
            // day.
            migrationBuilder.AddCheckConstraint(
                name: "ck_expedition_roster_dates",
                table: "expedition_roster",
                sql: "to_date IS NULL OR to_date >= from_date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_trip_logs_dates",
                table: "trip_logs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_expedition_roster_dates",
                table: "expedition_roster");

            // Back to the rule under which one day was stored as no last day, so the rows that
            // say one day the new way have to say it the old way before the old constraint fits.
            migrationBuilder.Sql(
                "UPDATE expedition_roster SET to_date = NULL WHERE to_date = from_date;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_expedition_roster_dates",
                table: "expedition_roster",
                sql: "to_date IS NULL OR to_date > from_date");
        }
    }
}
