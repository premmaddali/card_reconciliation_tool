using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations
{
    /// <inheritdoc />
    public partial class DualSettlementLegs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReconPassSummaries_ReconRunId_Ccy_Day",
                table: "ReconPassSummaries");

            migrationBuilder.AddColumn<byte>(
                name: "SettlementLeg",
                table: "ReconPassSummaries",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<byte>(
                name: "SettlementLeg",
                table: "ReconMatches",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<byte>(
                name: "SettlementLeg",
                table: "ReconExceptions",
                type: "tinyint",
                nullable: true,
                defaultValue: (byte)1);

            migrationBuilder.CreateIndex(
                name: "IX_ReconPassSummaries_ReconRunId_SettlementLeg_Ccy_Day",
                table: "ReconPassSummaries",
                columns: new[] { "ReconRunId", "SettlementLeg", "Ccy", "Day" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReconPassSummaries_ReconRunId_SettlementLeg_Ccy_Day",
                table: "ReconPassSummaries");

            migrationBuilder.DropColumn(
                name: "SettlementLeg",
                table: "ReconPassSummaries");

            migrationBuilder.DropColumn(
                name: "SettlementLeg",
                table: "ReconMatches");

            migrationBuilder.DropColumn(
                name: "SettlementLeg",
                table: "ReconExceptions");

            migrationBuilder.CreateIndex(
                name: "IX_ReconPassSummaries_ReconRunId_Ccy_Day",
                table: "ReconPassSummaries",
                columns: new[] { "ReconRunId", "Ccy", "Day" });
        }
    }
}
