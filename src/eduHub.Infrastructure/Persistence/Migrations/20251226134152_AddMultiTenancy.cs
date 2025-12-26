using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace eduHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var defaultOrgId = new Guid("3f8b6d78-1f7c-4b8a-9a8a-0b6d0d7d4f14");

            migrationBuilder.CreateTable(
                name: "organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LogoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PrimaryColor = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Timezone = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    SubscriptionPlan = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_organizations_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audit_logs_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "organization_members",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    JoinedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_members", x => new { x.OrganizationId, x.UserId });
                    table.ForeignKey(
                        name: "FK_organization_members_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_organization_members_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "organization_invites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UsedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_invites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_organization_invites_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_organization_invites_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_organization_invites_users_UsedByUserId",
                        column: x => x.UsedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "buildings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "buildings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "rooms",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql($@"
INSERT INTO organizations (""Id"", ""Name"", ""Slug"", ""IsActive"", ""CreatedAtUtc"")
VALUES ('{defaultOrgId}', 'Default Organization', 'default', TRUE, NOW());
");

            migrationBuilder.Sql($@"
UPDATE buildings SET ""OrganizationId"" = '{defaultOrgId}' WHERE ""OrganizationId"" IS NULL;
UPDATE rooms SET ""OrganizationId"" = '{defaultOrgId}' WHERE ""OrganizationId"" IS NULL;
UPDATE reservations SET ""OrganizationId"" = '{defaultOrgId}' WHERE ""OrganizationId"" IS NULL;
");

            migrationBuilder.Sql($@"
INSERT INTO organization_members (""OrganizationId"", ""UserId"", ""Role"", ""Status"", ""JoinedAtUtc"")
SELECT '{defaultOrgId}', ""Id"", CASE WHEN ""Role"" = 1 THEN 2 ELSE 0 END, 0, NOW()
FROM users;
");

            migrationBuilder.AlterColumn<Guid>(
                name: "OrganizationId",
                table: "buildings",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OrganizationId",
                table: "rooms",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OrganizationId",
                table: "reservations",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_buildings_OrganizationId_Name",
                table: "buildings",
                columns: new[] { "OrganizationId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_rooms_OrganizationId_BuildingId",
                table: "rooms",
                columns: new[] { "OrganizationId", "BuildingId" });

            migrationBuilder.CreateIndex(
                name: "IX_reservations_OrganizationId_RoomId",
                table: "reservations",
                columns: new[] { "OrganizationId", "RoomId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_CreatedByUserId",
                table: "audit_logs",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_OrganizationId_CreatedAtUtc",
                table: "audit_logs",
                columns: new[] { "OrganizationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_org_members_UserId",
                table: "organization_members",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_org_invites_OrganizationId_Email",
                table: "organization_invites",
                columns: new[] { "OrganizationId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_org_invites_TokenHash",
                table: "organization_invites",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organization_invites_CreatedByUserId",
                table: "organization_invites",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_organization_invites_UsedByUserId",
                table: "organization_invites",
                column: "UsedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_organizations_CreatedByUserId",
                table: "organizations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_organizations_Slug",
                table: "organizations",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_buildings_organizations_OrganizationId",
                table: "buildings",
                column: "OrganizationId",
                principalTable: "organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reservations_organizations_OrganizationId",
                table: "reservations",
                column: "OrganizationId",
                principalTable: "organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_rooms_organizations_OrganizationId",
                table: "rooms",
                column: "OrganizationId",
                principalTable: "organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_buildings_organizations_OrganizationId",
                table: "buildings");

            migrationBuilder.DropForeignKey(
                name: "FK_reservations_organizations_OrganizationId",
                table: "reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_rooms_organizations_OrganizationId",
                table: "rooms");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "organization_invites");

            migrationBuilder.DropTable(
                name: "organization_members");

            migrationBuilder.DropTable(
                name: "organizations");

            migrationBuilder.DropIndex(
                name: "IX_rooms_OrganizationId_BuildingId",
                table: "rooms");

            migrationBuilder.DropIndex(
                name: "IX_reservations_OrganizationId_RoomId",
                table: "reservations");

            migrationBuilder.DropIndex(
                name: "IX_buildings_OrganizationId_Name",
                table: "buildings");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "buildings");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "buildings");
        }
    }
}
