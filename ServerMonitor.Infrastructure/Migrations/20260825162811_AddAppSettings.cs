using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ServerMonitor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CpuThreshold = table.Column<double>(type: "double precision", nullable: false),
                    MemoryThreshold = table.Column<double>(type: "double precision", nullable: false),
                    DiskThreshold = table.Column<double>(type: "double precision", nullable: false),
                    AlertsEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "AppSettings",
                columns: new[] { "Id", "AlertsEnabled", "CpuThreshold", "DiskThreshold", "MemoryThreshold" },
                values: new object[] { 1, true, 90.0, 90.0, 90.0 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");
        }
    }
}
