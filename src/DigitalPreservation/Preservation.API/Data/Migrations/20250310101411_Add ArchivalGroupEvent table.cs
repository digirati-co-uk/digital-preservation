using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Preservation.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddArchivalGroupEventtable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "archival_group_events",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    archival_group = table.Column<string>(type: "text", nullable: false),
                    import_job_result = table.Column<string>(type: "text", nullable: true),
                    from_version = table.Column<string>(type: "text", nullable: true),
                    to_version = table.Column<string>(type: "text", nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archival_group_events", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "archival_group_events");
        }
    }
}
