using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Preservation.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class SuppressSeedArchivalGroupEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "archival_group_events",
                keyColumn: "id",
                keyValue: -1,
                column: "suppressed",
                value: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "archival_group_events",
                keyColumn: "id",
                keyValue: -1,
                column: "suppressed",
                value: false);
        }
    }
}
