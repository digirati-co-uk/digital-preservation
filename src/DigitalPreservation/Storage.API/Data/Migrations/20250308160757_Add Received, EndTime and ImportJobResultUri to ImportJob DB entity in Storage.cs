using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storage.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReceivedEndTimeandImportJobResultUritoImportJobDBentityinStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "end_time",
                table: "import_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "import_job_result_uri",
                table: "import_jobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "received",
                table: "import_jobs",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "end_time",
                table: "import_jobs");

            migrationBuilder.DropColumn(
                name: "import_job_result_uri",
                table: "import_jobs");

            migrationBuilder.DropColumn(
                name: "received",
                table: "import_jobs");
        }
    }
}
