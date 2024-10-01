using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Preservation.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDepositFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "created_on",
                table: "deposits",
                newName: "created");

            migrationBuilder.AddColumn<bool>(
                name: "active",
                table: "deposits",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "archival_group_path_under_root",
                table: "deposits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "archival_group_proposed_name",
                table: "deposits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "exported",
                table: "deposits",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "exported_by",
                table: "deposits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "files",
                table: "deposits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_modified",
                table: "deposits",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "last_modified_by",
                table: "deposits",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "minted_id",
                table: "deposits",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "preserved",
                table: "deposits",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "preserved_by",
                table: "deposits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "deposits",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "submission_text",
                table: "deposits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "version_exported",
                table: "deposits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "version_preserved",
                table: "deposits",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "active",
                table: "deposits");

            migrationBuilder.DropColumn(
                name: "archival_group_path_under_root",
                table: "deposits");

            migrationBuilder.DropColumn(
                name: "archival_group_proposed_name",
                table: "deposits");

            migrationBuilder.DropColumn(
                name: "exported",
                table: "deposits");

            migrationBuilder.DropColumn(
                name: "exported_by",
                table: "deposits");

            migrationBuilder.DropColumn(
                name: "files",
                table: "deposits");

            migrationBuilder.DropColumn(
                name: "last_modified",
                table: "deposits");

            migrationBuilder.DropColumn(
                name: "last_modified_by",
                table: "deposits");

            migrationBuilder.DropColumn(
                name: "minted_id",
                table: "deposits");

            migrationBuilder.DropColumn(
                name: "preserved",
                table: "deposits");

            migrationBuilder.DropColumn(
                name: "preserved_by",
                table: "deposits");

            migrationBuilder.DropColumn(
                name: "status",
                table: "deposits");

            migrationBuilder.DropColumn(
                name: "submission_text",
                table: "deposits");

            migrationBuilder.DropColumn(
                name: "version_exported",
                table: "deposits");

            migrationBuilder.DropColumn(
                name: "version_preserved",
                table: "deposits");

            migrationBuilder.RenameColumn(
                name: "created",
                table: "deposits",
                newName: "created_on");
        }
    }
}
