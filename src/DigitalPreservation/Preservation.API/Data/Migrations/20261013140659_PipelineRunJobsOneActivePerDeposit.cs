using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Preservation.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class PipelineRunJobsOneActivePerDeposit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_pipeline_run_jobs_deposit_one_active",
                table: "pipeline_run_jobs",
                column: "deposit",
                unique: true,
                filter: "status IN ('waiting','processing','metadataCreated')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pipeline_run_jobs_deposit_one_active",
                table: "pipeline_run_jobs");
        }
    }
}
