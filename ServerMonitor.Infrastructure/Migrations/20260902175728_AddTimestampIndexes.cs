using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerMonitor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTimestampIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_MetricSnapshots_TimestampUtc",
                table: "MetricSnapshots",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_TimestampUtc",
                table: "Alerts",
                column: "TimestampUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MetricSnapshots_TimestampUtc",
                table: "MetricSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_Alerts_TimestampUtc",
                table: "Alerts");
        }
    }
}
