using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerMonitor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSecurityStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Every existing account gets its own stamp. The default above would leave them all
            // sharing one empty string, and a value shared by everyone cannot end one person's
            // sessions without ending everyone's.
            //
            // gen_random_uuid() is built into PostgreSQL from version 13; no extension needed.
            migrationBuilder.Sql(
                """UPDATE "Users" SET "SecurityStamp" = gen_random_uuid()::text;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Users");
        }
    }
}
