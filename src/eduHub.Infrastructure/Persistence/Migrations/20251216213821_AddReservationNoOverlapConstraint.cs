using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eduHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationNoOverlapConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            migrationBuilder.Sql(
                @"DO $$
BEGIN
    BEGIN
        ALTER TABLE reservations
        ADD CONSTRAINT ""EXCLUDE_reservations_room_time""
        EXCLUDE USING gist (
            ""RoomId"" WITH =,
            tstzrange(""StartTimeUtc"", ""EndTimeUtc"") WITH &&
        )
        WHERE (""Status"" IN (0, 1));
    EXCEPTION WHEN undefined_function THEN
        ALTER TABLE reservations
        ADD CONSTRAINT ""EXCLUDE_reservations_room_time""
        EXCLUDE USING gist (
            ""RoomId"" WITH =,
            tsrange(""StartTimeUtc"", ""EndTimeUtc"") WITH &&
        )
        WHERE (""Status"" IN (0, 1));
    END;
END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"ALTER TABLE reservations
DROP CONSTRAINT IF EXISTS ""EXCLUDE_reservations_room_time"";");
        }
    }
}
