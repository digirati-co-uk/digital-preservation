using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storage.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCommitStartedtoImportJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "commit_started",
                table: "import_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "commit_transaction",
                table: "import_jobs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "commit_started",
                table: "import_jobs");

            migrationBuilder.DropColumn(
                name: "commit_transaction",
                table: "import_jobs");
        }
    }
}
