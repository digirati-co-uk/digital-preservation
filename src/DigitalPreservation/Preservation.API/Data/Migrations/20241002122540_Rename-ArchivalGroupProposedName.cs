using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Preservation.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameArchivalGroupProposedName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "archival_group_proposed_name",
                table: "deposits",
                newName: "archival_group_name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "archival_group_name",
                table: "deposits",
                newName: "archival_group_proposed_name");
        }
    }
}
