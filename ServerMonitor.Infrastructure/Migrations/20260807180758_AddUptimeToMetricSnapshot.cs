using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerMonitor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUptimeToMetricSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "UptimeSeconds",
                table: "MetricSnapshots",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UptimeSeconds",
                table: "MetricSnapshots");
        }
    }
}
