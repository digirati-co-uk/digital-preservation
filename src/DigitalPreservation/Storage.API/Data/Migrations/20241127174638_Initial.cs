using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storage.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_jobs",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    archival_group = table.Column<string>(type: "text", nullable: false),
                    import_job_json = table.Column<string>(type: "text", nullable: false),
                    import_job_result_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_jobs", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_jobs");
        }
    }
}
