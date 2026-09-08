using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerMonitor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHeartbeatSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HeartbeatAlertsEnabled",
                table: "AppSettings",
                type: "boolean",
                nullable: false,
                // A real column default rather than the CLR zero: the settings row is not
                // necessarily the seeded one, and it deserves a meaningful value.
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "OfflineAfterSeconds",
                table: "AppSettings",
                type: "integer",
                nullable: false,
                defaultValue: 300);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "HeartbeatAlertsEnabled", "OfflineAfterSeconds" },
                values: new object[] { true, 300 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeartbeatAlertsEnabled",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "OfflineAfterSeconds",
                table: "AppSettings");
        }
    }
}
