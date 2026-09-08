using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ServerMonitor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ServerId",
                table: "MetricSnapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ServerId",
                table: "Alerts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Servers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    OperatingSystem = table.Column<string>(type: "text", nullable: true),
                    AgentVersion = table.Column<string>(type: "text", nullable: true),
                    ApiKeyHash = table.Column<string>(type: "text", nullable: false),
                    RegisteredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Servers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetricSnapshots_ServerId_TimestampUtc",
                table: "MetricSnapshots",
                columns: new[] { "ServerId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_ServerId",
                table: "Alerts",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_ApiKeyHash",
                table: "Servers",
                column: "ApiKeyHash");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_PublicId",
                table: "Servers",
                column: "PublicId",
                unique: true);

            // Attach the existing history to a placeholder row BEFORE creating the foreign
            // keys, or the database will reject rows with ServerId = 0 that have no parent.
            // The name is fixed because a migration is static SQL: Environment.MachineName
            // would write the developer's hostname into everyone else's database.
            // The row gets its real name when the first agent registers and adopts it.
            migrationBuilder.Sql("""
                INSERT INTO "Servers" ("PublicId", "Name", "ApiKeyHash", "RegisteredAtUtc")
                VALUES (gen_random_uuid(), 'this-machine', '', now());

                UPDATE "MetricSnapshots" SET "ServerId" = (SELECT MIN("Id") FROM "Servers");
                UPDATE "Alerts" SET "ServerId" = (SELECT MIN("Id") FROM "Servers");
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Alerts_Servers_ServerId",
                table: "Alerts",
                column: "ServerId",
                principalTable: "Servers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MetricSnapshots_Servers_ServerId",
                table: "MetricSnapshots",
                column: "ServerId",
                principalTable: "Servers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Alerts_Servers_ServerId",
                table: "Alerts");

            migrationBuilder.DropForeignKey(
                name: "FK_MetricSnapshots_Servers_ServerId",
                table: "MetricSnapshots");

            migrationBuilder.DropTable(
                name: "Servers");

            migrationBuilder.DropIndex(
                name: "IX_MetricSnapshots_ServerId_TimestampUtc",
                table: "MetricSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_Alerts_ServerId",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "ServerId",
                table: "MetricSnapshots");

            migrationBuilder.DropColumn(
                name: "ServerId",
                table: "Alerts");
        }
    }
}
