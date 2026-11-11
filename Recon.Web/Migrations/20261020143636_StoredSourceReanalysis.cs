using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations
{
    /// <inheritdoc />
    public partial class StoredSourceReanalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AnalysisOfRunId",
                table: "ReconRuns",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconRuns_AnalysisOfRunId",
                table: "ReconRuns",
                column: "AnalysisOfRunId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReconRuns_ReconRuns_AnalysisOfRunId",
                table: "ReconRuns",
                column: "AnalysisOfRunId",
                principalTable: "ReconRuns",
                principalColumn: "ReconRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReconRuns_ReconRuns_AnalysisOfRunId",
                table: "ReconRuns");

            migrationBuilder.DropIndex(
                name: "IX_ReconRuns_AnalysisOfRunId",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "AnalysisOfRunId",
                table: "ReconRuns");
        }
    }
}
