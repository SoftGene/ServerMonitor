using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerMonitor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertSeverity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CpuWarningThreshold",
                table: "AppSettings",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "DiskWarningThreshold",
                table: "AppSettings",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "MemoryWarningThreshold",
                table: "AppSettings",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "Severity",
                table: "Alerts",
                type: "text",
                nullable: false,
                // Every alert written before levels existed fired at the only threshold there was,
                // which is the critical one now. An empty default would not merely be wrong: the
                // enum converter cannot parse it, so the whole journal would fail to load.
                defaultValue: "Critical");

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CpuWarningThreshold", "DiskWarningThreshold", "MemoryWarningThreshold" },
                values: new object[] { 75.0, 75.0, 75.0 });

            // A fixed 75 suits the seeded critical threshold of 90 and is wrong for anyone who has
            // changed theirs: critical at 70 would get a warning above critical, which can never
            // fire and which the settings page refuses to save. Deriving it from the critical
            // threshold already in place gives the same 75 for the default and a sane value otherwise.
            migrationBuilder.Sql(
                """
                UPDATE "AppSettings" SET
                    "CpuWarningThreshold" = GREATEST(1, "CpuThreshold" - 15),
                    "MemoryWarningThreshold" = GREATEST(1, "MemoryThreshold" - 15),
                    "DiskWarningThreshold" = GREATEST(1, "DiskThreshold" - 15);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CpuWarningThreshold",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DiskWarningThreshold",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "MemoryWarningThreshold",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "Severity",
                table: "Alerts");
        }
    }
}
