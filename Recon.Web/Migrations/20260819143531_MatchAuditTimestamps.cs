using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations
{
    /// <inheritdoc />
    public partial class MatchAuditTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "GlPostingDateTime",
                table: "ReconMatches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessorProductType",
                table: "ReconMatches",
                type: "nvarchar(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessorTransactionDateTime",
                table: "ReconMatches",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GlPostingDateTime",
                table: "ReconMatches");

            migrationBuilder.DropColumn(
                name: "ProcessorProductType",
                table: "ReconMatches");

            migrationBuilder.DropColumn(
                name: "ProcessorTransactionDateTime",
                table: "ReconMatches");
        }
    }
}
