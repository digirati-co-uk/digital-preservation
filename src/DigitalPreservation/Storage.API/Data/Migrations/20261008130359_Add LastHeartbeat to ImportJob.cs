using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storage.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLastHeartbeattoImportJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_heartbeat",
                table: "import_jobs",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_heartbeat",
                table: "import_jobs");
        }
    }
}
