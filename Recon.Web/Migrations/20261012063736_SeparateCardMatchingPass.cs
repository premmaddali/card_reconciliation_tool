using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations
{
    /// <inheritdoc />
    public partial class SeparateCardMatchingPass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CardOfRunId",
                table: "ReconRuns",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconRuns_CardOfRunId",
                table: "ReconRuns",
                column: "CardOfRunId",
                unique: true,
                filter: "[CardOfRunId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReconRuns_CardOfRunId",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "CardOfRunId",
                table: "ReconRuns");
        }
    }
}
