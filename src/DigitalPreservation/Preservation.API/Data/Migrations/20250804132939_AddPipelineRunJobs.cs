using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Preservation.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineRunJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pipeline_run_jobs",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    pipeline_job_result_id = table.Column<string>(type: "text", nullable: false),
                    deposit = table.Column<string>(type: "text", nullable: false),
                    archival_group = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_submitted = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    date_begun = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    date_finished = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    pipeline_job_json = table.Column<string>(type: "text", nullable: false),
                    latest_preservation_api_result_json = table.Column<string>(type: "text", nullable: false),
                    source_version = table.Column<string>(type: "text", nullable: true),
                    new_version = table.Column<string>(type: "text", nullable: true),
                    errors = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pipeline_run_jobs", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pipeline_run_jobs");
        }
    }
}
