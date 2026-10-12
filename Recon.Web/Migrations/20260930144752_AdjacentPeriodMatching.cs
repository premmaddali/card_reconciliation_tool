using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations
{
    /// <inheritdoc />
    public partial class AdjacentPeriodMatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAdjacentPeriodRun",
                table: "ReconRuns",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "GlSourceBatchId",
                table: "ReconMatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MatchedAcrossPeriods",
                table: "ReconMatches",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAdjacentPeriodRun",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "GlSourceBatchId",
                table: "ReconMatches");

            migrationBuilder.DropColumn(
                name: "MatchedAcrossPeriods",
                table: "ReconMatches");
        }
    }
}
