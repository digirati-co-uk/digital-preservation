using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Preservation.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddArchivedColumnToDeposit_And_AddNewArchiverTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "archived",
                table: "deposits",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "deposit_archive_jobs",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    deposit_uri = table.Column<string>(type: "text", nullable: false),
                    deposit_id = table.Column<string>(type: "text", nullable: false),
                    start_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_count = table.Column<int>(type: "integer", nullable: false),
                    errors = table.Column<string>(type: "text", nullable: true),
                    batch_number = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deposit_archive_jobs", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deposit_archive_jobs");

            migrationBuilder.DropColumn(
                name: "archived",
                table: "deposits");
        }
    }
}
