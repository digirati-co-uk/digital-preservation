using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Preservation.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddArchivalGroupEventseeddatabackstopevent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "archival_group_events",
                columns: new[] { "id", "archival_group", "deleted", "event_date", "from_version", "import_job_result", "to_version" },
                values: new object[] { -1, "https://example.com/archival-group", false, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "archival_group_events",
                keyColumn: "id",
                keyValue: -1);
        }
    }
}
