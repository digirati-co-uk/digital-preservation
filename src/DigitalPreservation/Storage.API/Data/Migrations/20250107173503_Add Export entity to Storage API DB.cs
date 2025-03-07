using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storage.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExportentitytoStorageAPIDB : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "export_results",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    archival_group = table.Column<string>(type: "text", nullable: false),
                    destination = table.Column<string>(type: "text", nullable: false),
                    date_begun = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    date_finished = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    export_result_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_export_results", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "export_results");
        }
    }
}
