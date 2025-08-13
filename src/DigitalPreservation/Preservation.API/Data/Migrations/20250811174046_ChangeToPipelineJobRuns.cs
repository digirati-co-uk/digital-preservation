using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Preservation.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChangeToPipelineJobRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "latest_preservation_api_result_json",
                table: "pipeline_run_jobs");

            migrationBuilder.DropColumn(
                name: "new_version",
                table: "pipeline_run_jobs");

            migrationBuilder.DropColumn(
                name: "pipeline_job_result_id",
                table: "pipeline_run_jobs");

            migrationBuilder.DropColumn(
                name: "source_version",
                table: "pipeline_run_jobs");

            migrationBuilder.AlterColumn<string>(
                name: "archival_group",
                table: "pipeline_run_jobs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "archival_group",
                table: "pipeline_run_jobs",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "latest_preservation_api_result_json",
                table: "pipeline_run_jobs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "new_version",
                table: "pipeline_run_jobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pipeline_job_result_id",
                table: "pipeline_run_jobs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "source_version",
                table: "pipeline_run_jobs",
                type: "text",
                nullable: true);
        }
    }
}
