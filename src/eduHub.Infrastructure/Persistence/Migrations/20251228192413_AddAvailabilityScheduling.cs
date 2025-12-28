using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace eduHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAvailabilityScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAtUtc",
                table: "reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE reservations
SET ""ExpiresAtUtc"" = ""CreatedAtUtc"" + interval '24 hours'
WHERE ""Status"" = 0 AND ""ExpiresAtUtc"" IS NULL;
");

            migrationBuilder.CreateTable(
                name: "availability_blackouts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildingId = table.Column<int>(type: "integer", nullable: true),
                    RoomId = table.Column<int>(type: "integer", nullable: true),
                    StartTimeUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndTimeUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_availability_blackouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_availability_blackouts_buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_availability_blackouts_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_availability_blackouts_rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_availability_blackouts_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "building_availability_windows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BuildingId = table.Column<int>(type: "integer", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartMinute = table.Column<int>(type: "integer", nullable: false),
                    EndMinute = table.Column<int>(type: "integer", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_building_availability_windows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_building_availability_windows_buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_building_availability_windows_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "room_availability_windows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoomId = table.Column<int>(type: "integer", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartMinute = table.Column<int>(type: "integer", nullable: false),
                    EndMinute = table.Column<int>(type: "integer", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_room_availability_windows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_room_availability_windows_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_room_availability_windows_rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_availability_blackouts_BuildingId",
                table: "availability_blackouts",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_availability_blackouts_CreatedByUserId",
                table: "availability_blackouts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_availability_blackouts_OrganizationId_StartTimeUtc",
                table: "availability_blackouts",
                columns: new[] { "OrganizationId", "StartTimeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_availability_blackouts_RoomId",
                table: "availability_blackouts",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_building_availability_windows_BuildingId_DayOfWeek",
                table: "building_availability_windows",
                columns: new[] { "BuildingId", "DayOfWeek" });

            migrationBuilder.CreateIndex(
                name: "IX_building_availability_windows_OrganizationId",
                table: "building_availability_windows",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_room_availability_windows_OrganizationId",
                table: "room_availability_windows",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_room_availability_windows_RoomId_DayOfWeek",
                table: "room_availability_windows",
                columns: new[] { "RoomId", "DayOfWeek" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "availability_blackouts");

            migrationBuilder.DropTable(
                name: "building_availability_windows");

            migrationBuilder.DropTable(
                name: "room_availability_windows");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "reservations");
        }
    }
}
