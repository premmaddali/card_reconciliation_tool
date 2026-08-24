using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations
{
    /// <inheritdoc />
    public partial class AtmNetworkValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AtmNetworkValidations",
                columns: table => new
                {
                    AtmNetworkValidationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReconRunId = table.Column<int>(type: "int", nullable: false),
                    NetworkStagingId = table.Column<int>(type: "int", nullable: true),
                    AtmStagingId = table.Column<int>(type: "int", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    NetworkReferenceCount = table.Column<int>(type: "int", nullable: false),
                    AtmReferenceCount = table.Column<int>(type: "int", nullable: false),
                    NetworkAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    NetworkCcy = table.Column<int>(type: "int", nullable: true),
                    NetworkFee = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    NetworkDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AtmAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    AtmCcy = table.Column<int>(type: "int", nullable: true),
                    AtmFee = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    AtmDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Narrative = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtmNetworkValidations", x => x.AtmNetworkValidationId);
                    table.ForeignKey(
                        name: "FK_AtmNetworkValidations_ReconRuns_ReconRunId",
                        column: x => x.ReconRunId,
                        principalTable: "ReconRuns",
                        principalColumn: "ReconRunId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AtmNetworkValidations_ReconRunId_ReferenceNumber",
                table: "AtmNetworkValidations",
                columns: new[] { "ReconRunId", "ReferenceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_AtmNetworkValidations_ReconRunId_Status",
                table: "AtmNetworkValidations",
                columns: new[] { "ReconRunId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AtmNetworkValidations");
        }
    }
}
