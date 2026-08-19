using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations
{
    /// <inheritdoc />
    public partial class ManualMatchComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsManuallyMatched",
                table: "ReconMatches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ManuallyMatchedDate",
                table: "ReconMatches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchComment",
                table: "ReconMatches",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsManuallyMatched",
                table: "ReconMatches");

            migrationBuilder.DropColumn(
                name: "ManuallyMatchedDate",
                table: "ReconMatches");

            migrationBuilder.DropColumn(
                name: "MatchComment",
                table: "ReconMatches");
        }
    }
}
