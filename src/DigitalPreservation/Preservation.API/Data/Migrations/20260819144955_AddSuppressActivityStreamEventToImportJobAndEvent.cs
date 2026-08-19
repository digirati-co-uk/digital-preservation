using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Preservation.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSuppressActivityStreamEventToImportJobAndEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "suppress_activity_stream_event",
                table: "import_jobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "suppressed",
                table: "archival_group_events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "archival_group_events",
                keyColumn: "id",
                keyValue: -1,
                column: "suppressed",
                value: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "suppress_activity_stream_event",
                table: "import_jobs");

            migrationBuilder.DropColumn(
                name: "suppressed",
                table: "archival_group_events");
        }
    }
}
