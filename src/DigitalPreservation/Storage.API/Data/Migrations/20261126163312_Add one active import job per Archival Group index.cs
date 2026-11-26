using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storage.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddoneactiveimportjobperArchivalGroupindex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The index can only be created if no Archival Group already has two active jobs: the race
            // this index closes (issue #460) may have left some. Keep the job most likely to be the
            // real one - the one with the latest heartbeat, then the newest - and end the others the
            // way the reaper does: inactive, completedWithErrors, with an error saying why.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT id, row_number() OVER (
                        PARTITION BY archival_group
                        ORDER BY last_heartbeat DESC NULLS LAST, received DESC NULLS LAST, id DESC) AS rn
                    FROM import_jobs
                    WHERE active
                )
                UPDATE import_jobs j
                SET active = false,
                    end_time = now(),
                    import_job_result_json = CASE
                        WHEN j.import_job_result_json IS NULL THEN NULL
                        ELSE (j.import_job_result_json::jsonb || jsonb_build_object(
                            'status', 'completedWithErrors',
                            'dateFinished', to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
                            'errors', COALESCE(NULLIF(j.import_job_result_json::jsonb -> 'errors', 'null'::jsonb), '[]'::jsonb)
                                || jsonb_build_array(jsonb_build_object('message',
                                    'Ended when one active import job per Archival Group became a database rule (#460): ' ||
                                    'another import job for this Archival Group was active too.'))))::text
                    END
                FROM ranked r
                WHERE j.id = r.id AND r.rn > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_import_jobs_archival_group_one_active",
                table: "import_jobs",
                column: "archival_group",
                unique: true,
                filter: "active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_import_jobs_archival_group_one_active",
                table: "import_jobs");
        }
    }
}
