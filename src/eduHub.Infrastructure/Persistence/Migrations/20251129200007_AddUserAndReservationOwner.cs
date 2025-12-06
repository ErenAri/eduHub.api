using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace eduHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAndReservationOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Replace failed type conversion (uuid -> int) with drop and re-add as nullable integer
            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "reservations");

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "reservations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reservations_CreatedByUserId",
                table: "reservations",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_reservations_Users_CreatedByUserId",
                table: "reservations",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reservations_Users_CreatedByUserId",
                table: "reservations");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropIndex(
                name: "IX_reservations_CreatedByUserId",
                table: "reservations");

            // Recreate original uuid column (non-nullable) to match the initial schema
            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "reservations");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "reservations",
                type: "uuid",
                nullable: false);
        }
    }
}
