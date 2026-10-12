using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations
{
    /// <inheritdoc />
    public partial class SecondaryDateAmountCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ValueDate",
                table: "StgGlEntries",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ValueDate",
                table: "StgGlBulkPostings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecondaryAmbiguousCount",
                table: "ReconRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SecondaryMatchCount",
                table: "ReconRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SecondaryOfRunId",
                table: "ReconRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "GlValueDate",
                table: "ReconMatches",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconRuns_SecondaryOfRunId",
                table: "ReconRuns",
                column: "SecondaryOfRunId",
                unique: true,
                filter: "[SecondaryOfRunId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReconRuns_SecondaryOfRunId",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "ValueDate",
                table: "StgGlEntries");

            migrationBuilder.DropColumn(
                name: "ValueDate",
                table: "StgGlBulkPostings");

            migrationBuilder.DropColumn(
                name: "SecondaryAmbiguousCount",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "SecondaryMatchCount",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "SecondaryOfRunId",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "GlValueDate",
                table: "ReconMatches");
        }
    }
}
