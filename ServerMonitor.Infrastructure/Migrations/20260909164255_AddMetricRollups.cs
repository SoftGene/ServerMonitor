using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ServerMonitor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMetricRollups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MetricRollups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServerId = table.Column<int>(type: "integer", nullable: false),
                    HourUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SampleCount = table.Column<int>(type: "integer", nullable: false),
                    CpuAvgPercent = table.Column<double>(type: "double precision", nullable: false),
                    CpuMaxPercent = table.Column<double>(type: "double precision", nullable: false),
                    MemoryAvgPercent = table.Column<double>(type: "double precision", nullable: false),
                    MemoryMaxPercent = table.Column<double>(type: "double precision", nullable: false),
                    DiskAvgPercent = table.Column<double>(type: "double precision", nullable: false),
                    DiskMaxPercent = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricRollups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetricRollups_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetricRollups_ServerId_HourUtc",
                table: "MetricRollups",
                columns: new[] { "ServerId", "HourUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MetricRollups");
        }
    }
}
